using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DeltaList.Shared.Metrics;

public class MetricsCollector
{
    private readonly Meter _meter;

    // Counters
    private readonly Counter<long> _connectionsTotal;
    private readonly Counter<long> _messagesInTotal;
    private readonly Counter<long> _messagesOutTotal;
    private readonly Counter<long> _errorsTotal;
    private readonly Counter<long> _reconnectsTotal;

    // Histograms
    private readonly Histogram<double> _messageLatency;
    private readonly Histogram<double> _blacklistDeliveryLatency;
    private readonly Histogram<long> _batchSize;

    // Gauges (via ObservableGauge)
    private long _activeConnections;
    private long _queuedMessages;

    public MetricsCollector(string serviceName)
    {
        _meter = new Meter(serviceName, "1.0.0");

        // Initialize counters
        _connectionsTotal = _meter.CreateCounter<long>(
            "connections_total",
            description: "Total number of device connections");

        _messagesInTotal = _meter.CreateCounter<long>(
            "messages_in_total",
            description: "Total number of messages received from devices");

        _messagesOutTotal = _meter.CreateCounter<long>(
            "messages_out_total",
            description: "Total number of messages sent to devices");

        _errorsTotal = _meter.CreateCounter<long>(
            "errors_total",
            description: "Total number of errors");

        _reconnectsTotal = _meter.CreateCounter<long>(
            "reconnects_total",
            description: "Total number of device reconnections");

        // Initialize histograms
        _messageLatency = _meter.CreateHistogram<double>(
            "message_latency_ms",
            unit: "ms",
            description: "Message processing latency in milliseconds");

        _blacklistDeliveryLatency = _meter.CreateHistogram<double>(
            "blacklist_delivery_latency_ms",
            unit: "ms",
            description: "Blacklist delta delivery latency in milliseconds");

        _batchSize = _meter.CreateHistogram<long>(
            "batch_size",
            description: "Number of events per batch");

        // Initialize gauges
        _meter.CreateObservableGauge(
            "active_connections",
            () => _activeConnections,
            description: "Current number of active connections");

        _meter.CreateObservableGauge(
            "queued_messages",
            () => _queuedMessages,
            description: "Current number of queued messages");
    }

    public void RecordConnection() => _connectionsTotal.Add(1);
    public void RecordDisconnection() => Interlocked.Decrement(ref _activeConnections);
    public void IncrementActiveConnections() => Interlocked.Increment(ref _activeConnections);
    public void RecordMessageIn() => _messagesInTotal.Add(1);
    public void RecordMessageOut() => _messagesOutTotal.Add(1);
    public void RecordError(string errorType) => _errorsTotal.Add(1, new KeyValuePair<string, object?>("type", errorType));
    public void RecordReconnect() => _reconnectsTotal.Add(1);

    public void RecordMessageLatency(double latencyMs, string messageType)
    {
        _messageLatency.Record(latencyMs, new KeyValuePair<string, object?>("type", messageType));
    }

    public void RecordBlacklistDeliveryLatency(double latencyMs)
    {
        _blacklistDeliveryLatency.Record(latencyMs);
    }

    public void RecordBatchSize(long size)
    {
        _batchSize.Record(size);
    }

    public void SetQueuedMessages(long count)
    {
        Interlocked.Exchange(ref _queuedMessages, count);
    }
}
