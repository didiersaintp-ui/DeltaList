using DeltaList.Shared.Messages;
using DeltaList.Shared.Services;
using Grpc.Core;
using Grpc.Net.Client;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using GrpcDeviceSimulator.Authentication;
using GrpcDeviceSimulator.Metrics;

namespace GrpcDeviceSimulator;

public class DeviceSimulator
{
    private readonly SimulatorOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, DeviceClient> _devices = new();
    private readonly Random _random = new();
    private readonly AdvancedMetricsCollector _metricsCollector;
    private readonly GrpcAuthClient? _authClient;

    // Metrics
    private long _totalBatchesSent = 0;
    private long _totalEventsSent = 0;
    private long _totalBlacklistDeltasReceived = 0;
    private long _totalReconnects = 0;
    private long _totalErrors = 0;

    public DeviceSimulator(SimulatorOptions options)
    {
        _options = options;
        _metricsCollector = new AdvancedMetricsCollector(_options.MetricsDirectory);

        if (_options.UseAuthentication && !string.IsNullOrEmpty(_options.AuthServerUrl))
        {
            _authClient = new GrpcAuthClient(_options.AuthServerUrl, _options.UseAuthentication);
            Log.Information("Authentication enabled with server: {AuthServer}", _options.AuthServerUrl);
        }
    }

    public GrpcAuthClient? AuthClient => _authClient;
    public AdvancedMetricsCollector MetricsCollector => _metricsCollector;

    public async Task RunAsync()
    {
        Log.Information("Starting gRPC Device Simulator");
        Log.Information("Server: {Server}", _options.ServerAddress);
        Log.Information("Devices: {DeviceCount}", _options.DeviceCount);
        Log.Information("Duration: {Duration}s", _options.DurationSeconds);
        Log.Information("Batch interval: {Interval}s", _options.BatchIntervalSeconds);
        Log.Information("Events per batch: {Count}", _options.EventsPerBatch);
        Log.Information("Test mode: {TestMode}", _options.TestMode);
        Log.Information("Authentication: {Auth}", _options.UseAuthentication ? "Enabled" : "Disabled");

        var stopwatch = Stopwatch.StartNew();

        // Start all device clients based on test mode
        var deviceTasks = new List<Task>();

        switch (_options.TestMode.ToLower())
        {
            case "burst":
                deviceTasks = await StartDevicesInBurstMode();
                break;
            case "staggered":
                deviceTasks = await StartDevicesInStaggeredMode();
                break;
            case "stress":
                deviceTasks = await StartDevicesInStressMode();
                break;
            default: // normal
                deviceTasks = await StartDevicesNormalMode();
                break;
        }

        // Start metrics reporter
        var metricsTask = ReportMetricsAsync(_cts.Token);

        // Wait for duration or Ctrl+C
        if (_options.DurationSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.DurationSeconds), _cts.Token);
            Log.Information("Simulation duration elapsed, stopping...");
            _cts.Cancel();
        }
        else
        {
            Log.Information("Running indefinitely. Press Ctrl+C to stop.");
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _cts.Cancel();
            };
            await Task.Delay(Timeout.Infinite, _cts.Token);
        }

        // Wait for all devices to stop
        await Task.WhenAll(deviceTasks);

        stopwatch.Stop();

        // Final report
        Log.Information("========== Final Report ==========");
        Log.Information("Duration: {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
        Log.Information("Devices: {Count}", _options.DeviceCount);
        Log.Information("Total batches sent: {Count}", _totalBatchesSent);
        Log.Information("Total events sent: {Count}", _totalEventsSent);
        Log.Information("Total blacklist deltas received: {Count}", _totalBlacklistDeltasReceived);
        Log.Information("Total reconnects: {Count}", _totalReconnects);
        Log.Information("Total errors: {Count}", _totalErrors);
        Log.Information("Avg throughput: {Rate:F2} events/sec", _totalEventsSent / stopwatch.Elapsed.TotalSeconds);

        // Export advanced metrics
        if (_options.ExportMetrics)
        {
            _metricsCollector.IncrementCounter("batches_sent", _totalBatchesSent);
            _metricsCollector.IncrementCounter("events_sent", _totalEventsSent);
            _metricsCollector.IncrementCounter("blacklist_deltas_received", _totalBlacklistDeltasReceived);
            _metricsCollector.IncrementCounter("reconnects", _totalReconnects);
            _metricsCollector.IncrementCounter("requests_failed", _totalErrors);
            _metricsCollector.IncrementCounter("requests_total", _totalBatchesSent + _totalErrors);

            _metricsCollector.PrintSummary();

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            await _metricsCollector.ExportToCsvAsync($"grpc_metrics_{timestamp}.csv");
            await _metricsCollector.ExportRawLatenciesAsync($"grpc_latencies_{timestamp}.csv");
        }
    }

    private async Task<List<Task>> StartDevicesNormalMode()
    {
        Log.Information("Starting devices in NORMAL mode");
        var deviceTasks = new List<Task>();

        for (int i = 0; i < _options.DeviceCount; i++)
        {
            var deviceId = $"{_options.DeviceIdPrefix}-{i:D6}";
            var client = new DeviceClient(deviceId, _options, this);
            _devices[deviceId] = client;

            // Stagger device starts to avoid thundering herd
            await Task.Delay(TimeSpan.FromMilliseconds(_options.StaggerDelayMs));

            deviceTasks.Add(client.RunAsync(_cts.Token));
        }

        return deviceTasks;
    }

    private async Task<List<Task>> StartDevicesInBurstMode()
    {
        Log.Information("Starting devices in BURST mode (all devices start simultaneously)");
        var deviceTasks = new List<Task>();

        // Create all clients first
        for (int i = 0; i < _options.DeviceCount; i++)
        {
            var deviceId = $"{_options.DeviceIdPrefix}-{i:D6}";
            var client = new DeviceClient(deviceId, _options, this);
            _devices[deviceId] = client;
        }

        // Start all simultaneously
        foreach (var client in _devices.Values)
        {
            deviceTasks.Add(client.RunAsync(_cts.Token));
        }

        await Task.Delay(100); // Small delay to let tasks start
        return deviceTasks;
    }

    private async Task<List<Task>> StartDevicesInStaggeredMode()
    {
        Log.Information("Starting devices in STAGGERED mode (longer delays between starts)");
        var deviceTasks = new List<Task>();
        var delayMs = Math.Max(_options.StaggerDelayMs * 5, 500); // 5x normal delay, min 500ms

        for (int i = 0; i < _options.DeviceCount; i++)
        {
            var deviceId = $"{_options.DeviceIdPrefix}-{i:D6}";
            var client = new DeviceClient(deviceId, _options, this);
            _devices[deviceId] = client;

            deviceTasks.Add(client.RunAsync(_cts.Token));

            await Task.Delay(TimeSpan.FromMilliseconds(delayMs));

            if (i % 10 == 0 && i > 0)
            {
                Log.Information("Started {Count}/{Total} devices", i, _options.DeviceCount);
            }
        }

        return deviceTasks;
    }

    private async Task<List<Task>> StartDevicesInStressMode()
    {
        Log.Information("Starting devices in STRESS mode (minimal delays, maximum load)");
        var deviceTasks = new List<Task>();

        // Reduce batch interval for stress mode
        if (_options.BatchIntervalSeconds > 10)
        {
            Log.Warning("Batch interval adjusted to 5s for stress mode");
        }

        // Create and start all clients with minimal delay
        for (int i = 0; i < _options.DeviceCount; i++)
        {
            var deviceId = $"{_options.DeviceIdPrefix}-{i:D6}";
            var client = new DeviceClient(deviceId, _options, this);
            _devices[deviceId] = client;

            deviceTasks.Add(client.RunAsync(_cts.Token));

            // Very minimal delay
            if (i % 100 == 0)
            {
                await Task.Delay(10);
            }
        }

        await Task.Delay(100);
        return deviceTasks;
    }

    private async Task ReportMetricsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long lastBatches = 0, lastEvents = 0;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);

            var elapsed = sw.Elapsed.TotalSeconds;
            var batches = Interlocked.Read(ref _totalBatchesSent);
            var events = Interlocked.Read(ref _totalEventsSent);
            var deltas = Interlocked.Read(ref _totalBlacklistDeltasReceived);

            var batchRate = (batches - lastBatches) / 10.0;
            var eventRate = (events - lastEvents) / 10.0;

            Log.Information(
                "[METRICS] Batches: {Batches} (+{BatchRate:F1}/s) | Events: {Events} (+{EventRate:F1}/s) | Deltas: {Deltas} | Reconnects: {Reconnects} | Errors: {Errors}",
                batches, batchRate, events, eventRate, deltas, _totalReconnects, _totalErrors);

            lastBatches = batches;
            lastEvents = events;
        }
    }

    public void IncrementBatchesSent() => Interlocked.Increment(ref _totalBatchesSent);
    public void IncrementEventsSent(int count) => Interlocked.Add(ref _totalEventsSent, count);
    public void IncrementDeltasReceived() => Interlocked.Increment(ref _totalBlacklistDeltasReceived);
    public void IncrementReconnects() => Interlocked.Increment(ref _totalReconnects);
    public void IncrementErrors() => Interlocked.Increment(ref _totalErrors);

    public bool ShouldSimulatePacketLoss() =>
        _options.PacketLossRate > 0 && _random.NextDouble() < _options.PacketLossRate;

    public int GetSimulatedLatency() =>
        _options.MaxLatencyMs > 0 ? _random.Next(_options.MaxLatencyMs) : 0;

    public bool ShouldReconnect() =>
        _options.ReconnectRate > 0 && _random.NextDouble() < _options.ReconnectRate;
}

public class DeviceClient
{
    private readonly string _deviceId;
    private readonly SimulatorOptions _options;
    private readonly DeviceSimulator _simulator;
    private readonly Random _random = new();
    private uint _batchSeq = 0;

    // Local blacklist state
    private readonly HashSet<string> _localBlacklist = new();

    public DeviceClient(string deviceId, SimulatorOptions options, DeviceSimulator simulator)
    {
        _deviceId = deviceId;
        _options = options;
        _simulator = simulator;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndStreamAsync(ct);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                // Normal cancellation
                break;
            }
            catch (Exception ex)
            {
                Log.Error("Device {DeviceId} error: {Message}", _deviceId, ex.Message);
                _simulator.IncrementErrors();

                // Reconnect after delay
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                _simulator.IncrementReconnects();
            }
        }
    }

    private async Task ConnectAndStreamAsync(CancellationToken ct)
    {
        // Get auth token if authentication is enabled
        string? authToken = null;
        if (_simulator.AuthClient != null)
        {
            authToken = await _simulator.AuthClient.RegisterAndGetTokenAsync(_deviceId, ct);
            if (authToken == null && _options.UseAuthentication)
            {
                Log.Warning("Device {DeviceId} failed to authenticate, continuing without token", _deviceId);
            }
        }

        // Create gRPC channel
        var channelOptions = new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 16 * 1024 * 1024,
            MaxSendMessageSize = 16 * 1024 * 1024
        };

        var address = _options.UseTls ? $"https://{_options.ServerAddress}" : $"http://{_options.ServerAddress}";

        using var channel = GrpcChannel.ForAddress(address, channelOptions);
        var client = new DeviceStreamService.DeviceStreamServiceClient(channel);

        // Create metadata with device ID and auth token
        var metadata = new Metadata
        {
            { "device-id", _deviceId }
        };

        if (!string.IsNullOrEmpty(authToken))
        {
            metadata.Add("authorization", $"Bearer {authToken}");
        }

        using var call = client.Connect(metadata, cancellationToken: ct);

        if (_options.Verbose)
            Log.Information("Device {DeviceId} connected", _deviceId);

        // Start receiving messages
        var receiveTask = ReceiveMessagesAsync(call.ResponseStream, ct);

        // Start sending batches
        var sendTask = SendBatchesAsync(call.RequestStream, ct);

        // Start heartbeats
        var heartbeatTask = SendHeartbeatsAsync(call.RequestStream, ct);

        // Wait for tasks or cancellation
        await Task.WhenAny(receiveTask, sendTask, heartbeatTask);

        // Complete the stream
        await call.RequestStream.CompleteAsync();

        if (_options.Verbose)
            Log.Information("Device {DeviceId} disconnected", _deviceId);
    }

    private async Task ReceiveMessagesAsync(IAsyncStreamReader<ServerMessage> stream, CancellationToken ct)
    {
        try
        {
            await foreach (var message in stream.ReadAllAsync(ct))
            {
                switch (message.PayloadCase)
                {
                    case ServerMessage.PayloadOneofCase.Delta:
                        await HandleBlacklistDeltaAsync(message.Delta);
                        break;

                    case ServerMessage.PayloadOneofCase.Command:
                        await HandleCommandAsync(message.Command);
                        break;

                    case ServerMessage.PayloadOneofCase.Ack:
                        if (_options.Verbose)
                            Log.Debug("Device {DeviceId} received ack for {MessageId}", _deviceId, message.Ack.MessageId);
                        break;
                }
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Normal cancellation
        }
    }

    private async Task SendBatchesAsync(IClientStreamWriter<DeviceMessage> stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Wait for batch interval
                await Task.Delay(TimeSpan.FromSeconds(_options.BatchIntervalSeconds), ct);

                // Measure latency
                var sw = Stopwatch.StartNew();

                // Simulate network latency
                var latency = _simulator.GetSimulatedLatency();
                if (latency > 0)
                    await Task.Delay(latency, ct);

                // Simulate packet loss
                if (_simulator.ShouldSimulatePacketLoss())
                {
                    if (_options.Verbose)
                        Log.Warning("Device {DeviceId} simulating packet loss", _deviceId);
                    continue;
                }

                // Create batch
                var batch = CreateBatch();
                var message = new DeviceMessage { Batch = batch };

                await stream.WriteAsync(message, ct);

                sw.Stop();
                _simulator.MetricsCollector.RecordLatency(sw.Elapsed.TotalMilliseconds);

                _simulator.IncrementBatchesSent();
                _simulator.IncrementEventsSent(batch.Events.Count);

                if (_options.Verbose)
                    Log.Debug("Device {DeviceId} sent batch {BatchSeq} in {Latency:F2}ms",
                        _deviceId, batch.BatchSeq, sw.Elapsed.TotalMilliseconds);

                // Simulate random reconnection
                if (_simulator.ShouldReconnect())
                {
                    Log.Information("Device {DeviceId} simulating random reconnection", _deviceId);
                    throw new RpcException(new Status(StatusCode.Aborted, "Simulated reconnection"));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SendHeartbeatsAsync(IClientStreamWriter<DeviceMessage> stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds), ct);

                var heartbeat = new Heartbeat
                {
                    DeviceId = _deviceId,
                    TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var message = new DeviceMessage { Heartbeat = heartbeat };
                await stream.WriteAsync(message, ct);

                if (_options.Verbose)
                    Log.Trace("Device {DeviceId} sent heartbeat", _deviceId);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private Batch CreateBatch()
    {
        var batch = new Batch
        {
            DeviceId = _deviceId,
            BatchTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            BatchSeq = ++_batchSeq
        };

        for (int i = 0; i < _options.EventsPerBatch; i++)
        {
            var evt = new Event
            {
                DeviceId = _deviceId,
                DeviceTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EventType = "transaction",
                TransactionId = Guid.NewGuid().ToString()
            };

            evt.Attributes.Add(new KeyValue { Key = "amount", Value = _random.Next(100, 100000).ToString() });
            evt.Attributes.Add(new KeyValue { Key = "merchant_id", Value = $"MERCH-{_random.Next(1000, 9999)}" });

            batch.Events.Add(evt);
        }

        return batch;
    }

    private Task HandleBlacklistDeltaAsync(BlacklistDelta delta)
    {
        // Apply delta to local blacklist
        foreach (var token in delta.Added)
        {
            _localBlacklist.Add(token);
        }

        foreach (var token in delta.Removed)
        {
            _localBlacklist.Remove(token);
        }

        _simulator.IncrementDeltasReceived();

        Log.Information(
            "Device {DeviceId} applied blacklist delta {DeltaId} (SeqNo: {SeqNo}): +{Added} -{Removed}. Total: {Total}",
            _deviceId, delta.DeltaId, delta.SeqNo, delta.Added.Count, delta.Removed.Count, _localBlacklist.Count);

        return Task.CompletedTask;
    }

    private Task HandleCommandAsync(Command command)
    {
        Log.Information(
            "Device {DeviceId} received command {CommandType}: {CommandId}",
            _deviceId, command.CommandType, command.CommandId);

        // Handle commands (e.g., reconnect, update config, etc.)
        return Task.CompletedTask;
    }
}
