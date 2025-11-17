using DeltaList.Shared.Services;
using DeltaList.Shared.Messages;
using DeltaList.Shared.Interfaces;
using DeltaList.Shared.Metrics;
using Grpc.Core;

namespace GrpcBackend.Services;

public class BlacklistAdminServiceImpl : BlacklistAdminService.BlacklistAdminServiceBase
{
    private readonly ILogger<BlacklistAdminServiceImpl> _logger;
    private readonly IBlacklistManager _blacklistManager;
    private readonly MetricsCollector _metrics;

    public BlacklistAdminServiceImpl(
        ILogger<BlacklistAdminServiceImpl> logger,
        IBlacklistManager blacklistManager,
        MetricsCollector metrics)
    {
        _logger = logger;
        _blacklistManager = blacklistManager;
        _metrics = metrics;
    }

    public override async Task<BlacklistUpdateResponse> AddToBlacklist(
        BlacklistUpdateRequest request,
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation(
                "Adding {Count} tokens to blacklist. Reason: {Reason}, By: {UpdatedBy}",
                request.PanTokens.Count, request.Reason, request.UpdatedBy);

            // Add to blacklist and get sequence number
            var (seqNo, _) = await _blacklistManager.AddToBlacklistAsync(
                request.PanTokens,
                request.ShardId,
                request.Reason,
                request.UpdatedBy,
                context.CancellationToken);

            // Get the delta that was created
            var deltas = await _blacklistManager.GetDeltasSinceAsync(
                seqNo - 1,
                request.ShardId,
                context.CancellationToken);
            var delta = deltas.FirstOrDefault();

            // Broadcast delta to all connected devices
            var deviceCount = DeviceStreamServiceImpl.GetActiveConnectionCount();
            if (delta != null)
            {
                await DeviceStreamServiceImpl.BroadcastBlacklistDeltaAsync(
                    delta,
                    _logger,
                    _metrics,
                    context.CancellationToken);
            }

            return new BlacklistUpdateResponse
            {
                Success = true,
                Message = $"Added {request.PanTokens.Count} tokens to blacklist",
                DeltaSeqNo = seqNo,
                DevicesNotified = deviceCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tokens to blacklist");
            _metrics.RecordError("blacklist_add_error");

            return new BlacklistUpdateResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                DeltaSeqNo = 0,
                DevicesNotified = 0
            };
        }
    }

    public override async Task<BlacklistUpdateResponse> RemoveFromBlacklist(
        BlacklistUpdateRequest request,
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation(
                "Removing {Count} tokens from blacklist. Reason: {Reason}, By: {UpdatedBy}",
                request.PanTokens.Count, request.Reason, request.UpdatedBy);

            // Remove from blacklist and get sequence number
            var (seqNo, _) = await _blacklistManager.RemoveFromBlacklistAsync(
                request.PanTokens,
                request.ShardId,
                request.Reason,
                request.UpdatedBy,
                context.CancellationToken);

            // Get the delta that was created
            var deltas = await _blacklistManager.GetDeltasSinceAsync(
                seqNo - 1,
                request.ShardId,
                context.CancellationToken);
            var delta = deltas.FirstOrDefault();

            // Broadcast delta to all connected devices
            var deviceCount = DeviceStreamServiceImpl.GetActiveConnectionCount();
            if (delta != null)
            {
                await DeviceStreamServiceImpl.BroadcastBlacklistDeltaAsync(
                    delta,
                    _logger,
                    _metrics,
                    context.CancellationToken);
            }

            return new BlacklistUpdateResponse
            {
                Success = true,
                Message = $"Removed {request.PanTokens.Count} tokens from blacklist",
                DeltaSeqNo = seqNo,
                DevicesNotified = deviceCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing tokens from blacklist");
            _metrics.RecordError("blacklist_remove_error");

            return new BlacklistUpdateResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                DeltaSeqNo = 0,
                DevicesNotified = 0
            };
        }
    }

    public override async Task<BlacklistSnapshot> GetBlacklist(
        BlacklistQuery request,
        ServerCallContext context)
    {
        try
        {
            // Get blacklist state for the requested shard
            var state = await _blacklistManager.GetBlacklistStateAsync(
                request.ShardId,
                context.CancellationToken);

            var snapshot = new BlacklistSnapshot
            {
                CurrentSeqNo = state.CurrentSeqNo,
                TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            snapshot.PanTokens.AddRange(state.PanTokens);

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blacklist");
            _metrics.RecordError("blacklist_get_error");
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }
}
