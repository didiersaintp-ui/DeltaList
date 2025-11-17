using DeltaList.Shared.Messages;
using DeltaList.Shared.Services;
using DeltaList.Shared.Metrics;
using DeltaList.Shared.Security;
using DeltaList.Shared.Interfaces;
using DeltaList.Shared.Models;
using Grpc.Core;
using Microsoft.Extensions.Caching.Distributed;
using System.Collections.Concurrent;
using System.Text.Json;

namespace GrpcBackend.Services;

public class DeviceStreamServiceImpl : DeviceStreamService.DeviceStreamServiceBase
{
    private readonly ILogger<DeviceStreamServiceImpl> _logger;
    private readonly IDistributedCache _cache;
    private readonly MetricsCollector _metrics;
    private readonly IEventStore _eventStore;
    private readonly IBlacklistManager _blacklistManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IRateLimiter _rateLimiter;

    // Track active device streams
    private static readonly ConcurrentDictionary<string, IServerStreamWriter<ServerMessage>> _deviceStreams = new();

    public DeviceStreamServiceImpl(
        ILogger<DeviceStreamServiceImpl> logger,
        IDistributedCache cache,
        MetricsCollector metrics,
        IEventStore eventStore,
        IBlacklistManager blacklistManager,
        IJwtTokenService jwtTokenService,
        IDeviceRegistry deviceRegistry,
        IRateLimiter rateLimiter)
    {
        _logger = logger;
        _cache = cache;
        _metrics = metrics;
        _eventStore = eventStore;
        _blacklistManager = blacklistManager;
        _jwtTokenService = jwtTokenService;
        _deviceRegistry = deviceRegistry;
        _rateLimiter = rateLimiter;
    }

    public override async Task Connect(
        IAsyncStreamReader<DeviceMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream,
        ServerCallContext context)
    {
        var deviceId = string.Empty;
        var startTime = DateTime.UtcNow;

        try
        {
            // ========== JWT AUTHENTICATION ==========
            // Extract and validate JWT token from metadata
            var authHeader = context.RequestHeaders.GetValue("authorization");
            if (string.IsNullOrEmpty(authHeader))
            {
                _logger.LogWarning("Connection rejected: missing authorization header");
                _metrics.RecordError("missing_auth_token");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing authorization token"));
            }

            // Remove "Bearer " prefix if present
            var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authHeader.Substring(7)
                : authHeader;

            // Validate JWT token
            deviceId = _jwtTokenService.ValidateToken(token);
            if (string.IsNullOrEmpty(deviceId))
            {
                _logger.LogWarning("Connection rejected: invalid JWT token");
                _metrics.RecordError("invalid_auth_token");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or expired token"));
            }

            // Verify device is registered and active
            var isValid = await _deviceRegistry.ValidateDeviceAsync(deviceId, context.CancellationToken);
            if (!isValid)
            {
                _logger.LogWarning("Connection rejected: device {DeviceId} is not active or not registered", deviceId);
                _metrics.RecordError("device_not_active");
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Device is not active"));
            }

            var connectionId = Guid.NewGuid().ToString();

            _logger.LogInformation(
                "Device {DeviceId} authenticated and connected with connection {ConnectionId}",
                deviceId, connectionId);

            // Track metrics
            _metrics.RecordConnection();
            _metrics.IncrementActiveConnections();

            // Register stream for this device
            _deviceStreams.AddOrUpdate(deviceId, responseStream, (_, __) => responseStream);

            // Send initial blacklist state
            await SendInitialBlacklistAsync(deviceId, responseStream, context.CancellationToken);

            // Process incoming messages
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                await ProcessDeviceMessageAsync(deviceId, message, responseStream, context.CancellationToken);
            }

            _logger.LogInformation(
                "Device {DeviceId} disconnected cleanly after {Duration}s",
                deviceId,
                (DateTime.UtcNow - startTime).TotalSeconds);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            _logger.LogInformation("Device {DeviceId} connection cancelled", deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in device stream for {DeviceId}", deviceId);
            _metrics.RecordError("stream_error");
        }
        finally
        {
            // Cleanup
            if (!string.IsNullOrEmpty(deviceId))
            {
                _deviceStreams.TryRemove(deviceId, out _);
            }
            _metrics.RecordDisconnection();
        }
    }

    private async Task ProcessDeviceMessageAsync(
        string deviceId,
        DeviceMessage message,
        IServerStreamWriter<ServerMessage> responseStream,
        CancellationToken cancellationToken)
    {
        var receiveTime = DateTime.UtcNow;

        try
        {
            switch (message.PayloadCase)
            {
                case DeviceMessage.PayloadOneofCase.Batch:
                    await ProcessBatchAsync(deviceId, message.Batch, receiveTime, responseStream, cancellationToken);
                    break;

                case DeviceMessage.PayloadOneofCase.Heartbeat:
                    await ProcessHeartbeatAsync(deviceId, message.Heartbeat, cancellationToken);
                    break;

                case DeviceMessage.PayloadOneofCase.Ack:
                    await ProcessAckAsync(deviceId, message.Ack, cancellationToken);
                    break;

                default:
                    _logger.LogWarning("Unknown message type from device {DeviceId}", deviceId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from device {DeviceId}", deviceId);
            _metrics.RecordError("message_processing");
        }
    }

    private async Task ProcessBatchAsync(
        string deviceId,
        Batch batch,
        DateTime receiveTime,
        IServerStreamWriter<ServerMessage> responseStream,
        CancellationToken cancellationToken)
    {
        var processingStart = DateTime.UtcNow;

        _logger.LogDebug(
            "Processing batch {BatchSeq} from device {DeviceId} with {EventCount} events",
            batch.BatchSeq, deviceId, batch.Events.Count);

        // ========== RATE LIMITING ==========
        if (!_rateLimiter.AllowBatch(deviceId))
        {
            _logger.LogWarning(
                "Rate limit exceeded for device {DeviceId}, batch {BatchSeq} rejected",
                deviceId, batch.BatchSeq);
            _metrics.RecordError("rate_limit_exceeded");

            // Send negative acknowledgment
            var nack = new ServerMessage
            {
                Ack = new Ack
                {
                    MessageId = batch.BatchSeq.ToString(),
                    AckType = "batch",
                    Success = false,
                    ErrorMessage = $"Rate limit exceeded. Remaining quota: {_rateLimiter.GetRemainingQuota(deviceId)}"
                }
            };

            await responseStream.WriteAsync(nack, cancellationToken);
            return;
        }

        _metrics.RecordMessageIn();
        _metrics.RecordBatchSize(batch.Events.Count);

        // TODO: Verify signature
        // if (!VerifyBatchSignature(batch)) { ... }

        try
        {
            // Store events - convert protobuf events to EventRecord objects
            var eventRecords = batch.Events.Select(e => new EventRecord
            {
                DeviceId = deviceId,
                DeviceTimestampUtc = e.DeviceTimestampUtc,
                ReceivedTimestampUtc = ((DateTimeOffset)receiveTime).ToUnixTimeMilliseconds(),
                BatchSeq = batch.BatchSeq,
                EventType = e.EventType,
                Attributes = e.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value),
                TransactionId = e.TransactionId
            });

            await _eventStore.StoreEventsAsync(deviceId, eventRecords, cancellationToken);

            // Send acknowledgment
            var ack = new ServerMessage
            {
                Ack = new Ack
                {
                    MessageId = batch.BatchSeq.ToString(),
                    AckType = "batch",
                    Success = true
                }
            };

            await responseStream.WriteAsync(ack, cancellationToken);

            var latency = (DateTime.UtcNow - processingStart).TotalMilliseconds;
            _metrics.RecordMessageLatency(latency, "batch");

            _logger.LogDebug(
                "Batch {BatchSeq} from device {DeviceId} processed in {Latency}ms",
                batch.BatchSeq, deviceId, latency);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing batch {BatchSeq} from device {DeviceId}", batch.BatchSeq, deviceId);
            _metrics.RecordError("batch_storage_error");

            // Send error acknowledgment
            var errorAck = new ServerMessage
            {
                Ack = new Ack
                {
                    MessageId = batch.BatchSeq.ToString(),
                    AckType = "batch",
                    Success = false,
                    ErrorMessage = $"Storage error: {ex.Message}"
                }
            };

            await responseStream.WriteAsync(errorAck, cancellationToken);
        }
    }

    private async Task ProcessHeartbeatAsync(
        string deviceId,
        Heartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        _logger.LogTrace("Heartbeat from device {DeviceId}", deviceId);

        // Update last seen time in cache
        var cacheKey = $"device:lastseen:{deviceId}";
        await _cache.SetStringAsync(
            cacheKey,
            DateTime.UtcNow.Ticks.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
            cancellationToken);
    }

    private Task ProcessAckAsync(
        string deviceId,
        Ack ack,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Ack received from device {DeviceId} for {AckType} message {MessageId}: {Success}",
            deviceId, ack.AckType, ack.MessageId, ack.Success);

        if (!ack.Success)
        {
            _logger.LogWarning(
                "Device {DeviceId} failed to process {AckType} message {MessageId}: {Error}",
                deviceId, ack.AckType, ack.MessageId, ack.ErrorMessage);
            _metrics.RecordError("device_ack_failure");
        }

        return Task.CompletedTask;
    }

    private async Task SendInitialBlacklistAsync(
        string deviceId,
        IServerStreamWriter<ServerMessage> responseStream,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get blacklist state for shard 0 (simplified for PoC)
            var blacklist = await _blacklistManager.GetBlacklistStateAsync(0, cancellationToken);

            if (blacklist != null && blacklist.PanTokens.Any())
            {
                var delta = new BlacklistDelta
                {
                    DeltaId = Guid.NewGuid().ToString(),
                    SeqNo = blacklist.CurrentSeqNo,
                    TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ShardId = 0
                };

                delta.Added.AddRange(blacklist.PanTokens);

                var message = new ServerMessage { Delta = delta };
                await responseStream.WriteAsync(message, cancellationToken);

                _logger.LogInformation(
                    "Sent initial blacklist to device {DeviceId} with {Count} tokens",
                    deviceId, blacklist.PanTokens.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending initial blacklist to device {DeviceId}", deviceId);
        }
    }

    // Public method to broadcast blacklist delta to all connected devices
    public static async Task BroadcastBlacklistDeltaAsync(
        BlacklistDelta delta,
        ILogger logger,
        MetricsCollector metrics,
        CancellationToken cancellationToken = default)
    {
        var sendStart = DateTime.UtcNow;
        var successCount = 0;
        var failureCount = 0;

        var message = new ServerMessage { Delta = delta };

        foreach (var (deviceId, stream) in _deviceStreams)
        {
            try
            {
                await stream.WriteAsync(message, cancellationToken);
                successCount++;
                metrics.RecordMessageOut();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send blacklist delta to device {DeviceId}", deviceId);
                failureCount++;
                metrics.RecordError("delta_send_failure");
            }
        }

        var latency = (DateTime.UtcNow - sendStart).TotalMilliseconds;
        metrics.RecordBlacklistDeliveryLatency(latency);

        logger.LogInformation(
            "Broadcast blacklist delta {DeltaId} to {SuccessCount} devices ({FailureCount} failures) in {Latency}ms",
            delta.DeltaId, successCount, failureCount, latency);
    }

    public static int GetActiveConnectionCount() => _deviceStreams.Count;
}
