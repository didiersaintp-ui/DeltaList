using DeltaList.Shared.Configuration;
using DeltaList.Shared.Messages;
using DeltaList.Shared.Metrics;
using DeltaList.Shared.Interfaces;
using Google.Protobuf;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using System.Text;
using System.Text.Json;

namespace MqttBackend.Services;

public class MqttBrokerClient : IHostedService
{
    private readonly ILogger<MqttBrokerClient> _logger;
    private readonly BackendSettings _settings;
    private readonly IEventStore _eventStore;
    private readonly MetricsCollector _metrics;

    private IManagedMqttClient? _mqttClient;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private int _reconnectAttempts = 0;
    private const int MAX_RECONNECT_DELAY_SECONDS = 300; // 5 minutes

    public bool IsConnected => _mqttClient?.IsConnected ?? false;

    public MqttBrokerClient(
        ILogger<MqttBrokerClient> logger,
        BackendSettings settings,
        IEventStore eventStore,
        MetricsCollector metrics)
    {
        _logger = logger;
        _settings = settings;
        _eventStore = eventStore;
        _metrics = metrics;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MQTT Broker Client");
        _logger.LogInformation("Connecting to MQTT broker: {Broker}:{Port}",
            _settings.Mqtt.BrokerHost, _settings.Mqtt.BrokerPort);

        // Create MQTT client options
        var clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.Mqtt.BrokerHost, _settings.Mqtt.BrokerPort)
            .WithClientId(_settings.Mqtt.ClientId)
            .WithCleanSession(false)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_settings.Mqtt.KeepAlivePeriodSeconds));

        if (!string.IsNullOrEmpty(_settings.Mqtt.Username))
        {
            clientOptions.WithCredentials(_settings.Mqtt.Username, _settings.Mqtt.Password);
        }

        if (_settings.Mqtt.UseTls)
        {
            clientOptions.WithTls();
        }

        // Create managed client options with exponential backoff
        var managedOptions = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientOptions.Build())
            .WithAutoReconnectDelay(CalculateReconnectDelay())
            .WithMaxPendingMessages(_settings.Mqtt.MaxPendingMessages)
            .Build();

        // Create managed MQTT client
        var factory = new MqttFactory();
        _mqttClient = factory.CreateManagedMqttClient();

        // Setup event handlers
        _mqttClient.ConnectedAsync += OnConnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        // Start client
        await _mqttClient.StartAsync(managedOptions);

        // Subscribe to device event topics with configurable QoS
        var qosLevel = _settings.Mqtt.QoS switch
        {
            0 => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce,
            1 => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce
        };

        await _mqttClient.SubscribeAsync("devices/+/events", qosLevel);

        _logger.LogInformation("MQTT Broker Client started successfully (QoS: {QoS})", _settings.Mqtt.QoS);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MQTT Broker Client");

        if (_mqttClient != null)
        {
            await _mqttClient.StopAsync();
            _mqttClient.Dispose();
        }

        _logger.LogInformation("MQTT Broker Client stopped");
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _reconnectAttempts = 0; // Reset on successful connection
        _logger.LogInformation("Connected to MQTT broker successfully");
        _metrics.RecordConnection();
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _reconnectAttempts++;
        var delay = CalculateReconnectDelay();

        _logger.LogWarning(
            "Disconnected from MQTT broker: {Reason}. Reconnect attempt #{Attempt}, next retry in {Delay}s",
            args.Reason, _reconnectAttempts, delay.TotalSeconds);

        _metrics.RecordError("mqtt_disconnection");
        return Task.CompletedTask;
    }

    private TimeSpan CalculateReconnectDelay()
    {
        // Exponential backoff: 2^attempt seconds, capped at MAX_RECONNECT_DELAY_SECONDS
        var delaySeconds = Math.Min(Math.Pow(2, _reconnectAttempts), MAX_RECONNECT_DELAY_SECONDS);
        return TimeSpan.FromSeconds(delaySeconds);
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic;
            var payload = args.ApplicationMessage.Payload;

            _logger.LogDebug("Received message on topic: {Topic}, size: {Size} bytes", topic, payload.Length);

            // Extract device ID from topic: devices/{deviceId}/events
            var topicParts = topic.Split('/');
            if (topicParts.Length != 3 || topicParts[0] != "devices" || topicParts[2] != "events")
            {
                _logger.LogWarning("Invalid topic format: {Topic}", topic);
                return;
            }

            var deviceId = topicParts[1];

            // Parse Protobuf batch
            Batch batch;
            try
            {
                batch = Batch.Parser.ParseFrom(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Protobuf batch from device {DeviceId}", deviceId);
                _metrics.RecordError("batch_parse_error");
                return;
            }

            // Validate device ID matches
            if (batch.DeviceId != deviceId)
            {
                _logger.LogWarning(
                    "Device ID mismatch: topic={TopicDeviceId}, batch={BatchDeviceId}",
                    deviceId, batch.DeviceId);
                return;
            }

            // Store batch
            var receiveTime = DateTime.UtcNow;
            await _eventStore.StoreBatchAsync(batch, receiveTime);

            _metrics.RecordMessageIn();
            _metrics.RecordBatchSize(batch.Events.Count);

            _logger.LogDebug(
                "Stored batch {BatchSeq} from device {DeviceId} with {EventCount} events",
                batch.BatchSeq, deviceId, batch.Events.Count);

            // Send acknowledgment (optional - publish to devices/{deviceId}/commands)
            await PublishAckAsync(deviceId, batch.BatchSeq.ToString(), true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message");
            _metrics.RecordError("message_processing_error");
        }
    }

    public async Task PublishBlacklistDeltaAsync(DeltaList.Shared.Messages.BlacklistDelta delta)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
        {
            _logger.LogWarning("Cannot publish blacklist delta: MQTT client not connected");
            return;
        }

        try
        {
            // Serialize delta to Protobuf (already a Protobuf message)
            var payload = delta.ToByteArray();

            // Publish to broadcast topic (all devices subscribe to this)
            var topic = "blacklist/delta";

            var qosLevel = _settings.Mqtt.QoS switch
            {
                0 => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce,
                1 => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce,
                2 => MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce,
                _ => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce
            };

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qosLevel)
                .WithRetainFlag(_settings.Mqtt.RetainBlacklistDeltas) // Retain last delta for offline devices
                .Build();

            await _publishLock.WaitAsync();
            try
            {
                await _mqttClient.EnqueueAsync(message);
                _metrics.RecordMessageOut();

                _logger.LogInformation(
                    "Published blacklist delta {DeltaId} (SeqNo: {SeqNo}): +{Added} -{Removed}",
                    delta.DeltaId, delta.SeqNo, delta.Added.Count, delta.Removed.Count);
            }
            finally
            {
                _publishLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing blacklist delta");
            _metrics.RecordError("delta_publish_error");
        }
    }

    private async Task PublishAckAsync(string deviceId, string messageId, bool success)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
            return;

        try
        {
            var ack = new Ack
            {
                MessageId = messageId,
                AckType = "batch",
                Success = success
            };

            var payload = ack.ToByteArray();
            var topic = $"devices/{deviceId}/commands";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            await _mqttClient.EnqueueAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing ack to device {DeviceId}", deviceId);
        }
    }

    public async Task PublishCommandAsync(string deviceId, Command command)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
        {
            _logger.LogWarning("Cannot publish command: MQTT client not connected");
            return;
        }

        try
        {
            var payload = command.ToByteArray();
            var topic = $"devices/{deviceId}/commands";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _mqttClient.EnqueueAsync(message);
            _metrics.RecordMessageOut();

            _logger.LogInformation(
                "Published command {CommandType} to device {DeviceId}",
                command.CommandType, deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing command to device {DeviceId}", deviceId);
            _metrics.RecordError("command_publish_error");
        }
    }
}
