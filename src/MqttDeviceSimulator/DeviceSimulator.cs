using DeltaList.Shared.Messages;
using MQTTnet;
using MQTTnet.Client;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using MqttDeviceSimulator.Authentication;
using MqttDeviceSimulator.Metrics;

namespace MqttDeviceSimulator;

public class DeviceSimulator
{
    private readonly SimulatorOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, MqttDeviceClient> _devices = new();
    private readonly Random _random = new();
    private readonly AdvancedMetricsCollector _metricsCollector;
    private readonly MqttAuthClient? _authClient;

    // Metrics
    private long _totalBatchesSent = 0;
    private long _totalEventsSent = 0;
    private long _totalBlacklistDeltasReceived = 0;
    private long _totalReconnects = 0;
    private long _totalErrors = 0;
    private long _totalMessagesLost = 0;

    public DeviceSimulator(SimulatorOptions options)
    {
        _options = options;
        _metricsCollector = new AdvancedMetricsCollector(_options.MetricsDirectory);

        if (_options.UseAuthentication && !string.IsNullOrEmpty(_options.AuthServerUrl))
        {
            _authClient = new MqttAuthClient(_options.AuthServerUrl, _options.UseAuthentication);
            Log.Information("Authentication enabled with server: {AuthServer}", _options.AuthServerUrl);
        }
    }

    public MqttAuthClient? AuthClient => _authClient;
    public AdvancedMetricsCollector MetricsCollector => _metricsCollector;

    public async Task RunAsync()
    {
        Log.Information("Starting MQTT Device Simulator");
        Log.Information("Broker: {Host}:{Port}", _options.BrokerHost, _options.BrokerPort);
        Log.Information("Devices: {DeviceCount}", _options.DeviceCount);
        Log.Information("Duration: {Duration}s", _options.DurationSeconds);
        Log.Information("Batch interval: {Interval}s", _options.BatchIntervalSeconds);
        Log.Information("Events per batch: {Count}", _options.EventsPerBatch);
        Log.Information("QoS Level: {QoS}", _options.QosLevel);
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
        Log.Information("Total messages lost (QoS 0): {Count}", _totalMessagesLost);
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
            _metricsCollector.IncrementCounter("messages_lost", _totalMessagesLost);

            _metricsCollector.PrintSummary();

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            await _metricsCollector.ExportToCsvAsync($"mqtt_metrics_{timestamp}.csv");
            await _metricsCollector.ExportRawLatenciesAsync($"mqtt_latencies_{timestamp}.csv");
        }
    }

    private async Task<List<Task>> StartDevicesNormalMode()
    {
        Log.Information("Starting devices in NORMAL mode");
        var deviceTasks = new List<Task>();

        for (int i = 0; i < _options.DeviceCount; i++)
        {
            var deviceId = $"{_options.DeviceIdPrefix}-{i:D6}";
            var client = new MqttDeviceClient(deviceId, _options, this);
            _devices[deviceId] = client;

            await Task.Delay(TimeSpan.FromMilliseconds(_options.StaggerDelayMs));
            deviceTasks.Add(client.RunAsync(_cts.Token));
        }

        return deviceTasks;
    }

    private async Task<List<Task>> StartDevicesInBurstMode()
    {
        Log.Information("Starting devices in BURST mode (all devices start simultaneously)");
        var deviceTasks = new List<Task>();

        for (int i = 0; i < _options.DeviceCount; i++)
        {
            var deviceId = $"{_options.DeviceIdPrefix}-{i:D6}";
            var client = new MqttDeviceClient(deviceId, _options, this);
            _devices[deviceId] = client;
        }

        foreach (var client in _devices.Values)
        {
            deviceTasks.Add(client.RunAsync(_cts.Token));
        }

        await Task.Delay(100);
        return deviceTasks;
    }

    private async Task<List<Task>> StartDevicesInStaggeredMode()
    {
        Log.Information("Starting devices in STAGGERED mode (longer delays between starts)");
        var deviceTasks = new List<Task>();
        var delayMs = Math.Max(_options.StaggerDelayMs * 5, 500);

        for (int i = 0; i < _options.DeviceCount; i++)
        {
            var deviceId = $"{_options.DeviceIdPrefix}-{i:D6}";
            var client = new MqttDeviceClient(deviceId, _options, this);
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

        for (int i = 0; i < _options.DeviceCount; i++)
        {
            var deviceId = $"{_options.DeviceIdPrefix}-{i:D6}";
            var client = new MqttDeviceClient(deviceId, _options, this);
            _devices[deviceId] = client;

            deviceTasks.Add(client.RunAsync(_cts.Token));

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
    public void IncrementMessagesLost() => Interlocked.Increment(ref _totalMessagesLost);

    public bool ShouldSimulatePacketLoss() =>
        _options.PacketLossRate > 0 && _random.NextDouble() < _options.PacketLossRate;

    public int GetSimulatedLatency() =>
        _options.MaxLatencyMs > 0 ? _random.Next(_options.MaxLatencyMs) : 0;

    public bool ShouldReconnect() =>
        _options.ReconnectRate > 0 && _random.NextDouble() < _options.ReconnectRate;
}

public class MqttDeviceClient
{
    private readonly string _deviceId;
    private readonly SimulatorOptions _options;
    private readonly DeviceSimulator _simulator;
    private readonly Random _random = new();
    private uint _batchSeq = 0;

    // Local blacklist state
    private readonly HashSet<string> _localBlacklist = new();

    private IMqttClient? _mqttClient;

    public MqttDeviceClient(string deviceId, SimulatorOptions options, DeviceSimulator simulator)
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
                await ConnectAndRunAsync(ct);
            }
            catch (OperationCanceledException)
            {
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

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        // Create MQTT client
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        // Setup event handlers
        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

        // Create connection options
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.BrokerHost, _options.BrokerPort)
            .WithClientId(_deviceId)
            .WithCleanSession(false)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));

        if (!string.IsNullOrEmpty(_options.Username))
        {
            optionsBuilder.WithCredentials(_options.Username, _options.Password);
        }

        if (_options.UseTls)
        {
            optionsBuilder.WithTls();
        }

        var mqttOptions = optionsBuilder.Build();

        // Connect
        await _mqttClient.ConnectAsync(mqttOptions, ct);

        if (_options.Verbose)
            Log.Information("Device {DeviceId} connected to MQTT broker", _deviceId);

        // Subscribe to command and blacklist topics
        await _mqttClient.SubscribeAsync($"devices/{_deviceId}/commands", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, ct);
        await _mqttClient.SubscribeAsync("blacklist/delta", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, ct);

        // Start sending batches
        await SendBatchesAsync(ct);
    }

    private async Task SendBatchesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _mqttClient?.IsConnected == true)
        {
            try
            {
                // Wait for batch interval
                await Task.Delay(TimeSpan.FromSeconds(_options.BatchIntervalSeconds), ct);

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

                // Serialize to Protobuf
                var payload = batch.ToByteArray();

                // Publish to device events topic
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic($"devices/{_deviceId}/events")
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _mqttClient!.PublishAsync(message, ct);

                _simulator.IncrementBatchesSent();
                _simulator.IncrementEventsSent(batch.Events.Count);

                if (_options.Verbose)
                    Log.Debug("Device {DeviceId} sent batch {BatchSeq}", _deviceId, batch.BatchSeq);

                // Simulate random reconnection
                if (_simulator.ShouldReconnect())
                {
                    Log.Information("Device {DeviceId} simulating random reconnection", _deviceId);
                    await _mqttClient.DisconnectAsync(cancellationToken: ct);
                    throw new Exception("Simulated reconnection");
                }
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

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic;
            var payload = args.ApplicationMessage.Payload;

            if (topic == "blacklist/delta")
            {
                // Parse blacklist delta
                var delta = BlacklistDelta.Parser.ParseFrom(payload);
                HandleBlacklistDelta(delta);
            }
            else if (topic == $"devices/{_deviceId}/commands")
            {
                // Parse command or ack
                try
                {
                    var ack = Ack.Parser.ParseFrom(payload);
                    if (_options.Verbose)
                        Log.Debug("Device {DeviceId} received ack for {MessageId}", _deviceId, ack.MessageId);
                }
                catch
                {
                    // Might be a command
                    var command = Command.Parser.ParseFrom(payload);
                    if (_options.Verbose)
                        Log.Information("Device {DeviceId} received command {CommandType}", _deviceId, command.CommandType);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Device {DeviceId} error processing message", _deviceId);
        }

        return Task.CompletedTask;
    }

    private void HandleBlacklistDelta(BlacklistDelta delta)
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
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (_options.Verbose && args.Exception != null)
            Log.Warning("Device {DeviceId} disconnected: {Reason}", _deviceId, args.ReasonString);

        return Task.CompletedTask;
    }
}
