using DeltaList.Shared.Messages;
using DeltaList.Shared.Models;
using System.Collections.Concurrent;

namespace GrpcBackend.Services;

/// <summary>
/// In-memory event store for PoC - replace with Azure Blob/CosmosDB for production
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ILogger<InMemoryEventStore> _logger;
    private readonly ConcurrentBag<EventRecord> _events = new();

    public InMemoryEventStore(ILogger<InMemoryEventStore> logger)
    {
        _logger = logger;
    }

    public Task StoreBatchAsync(Batch batch, DateTime receivedAt)
    {
        var receivedTicks = new DateTimeOffset(receivedAt).ToUnixTimeMilliseconds();

        foreach (var evt in batch.Events)
        {
            var record = new EventRecord
            {
                Id = Guid.NewGuid().ToString(),
                DeviceId = evt.DeviceId,
                DeviceTimestampUtc = evt.DeviceTimestampUtc,
                ReceivedTimestampUtc = receivedTicks,
                BatchSeq = batch.BatchSeq,
                EventType = evt.EventType,
                TransactionId = evt.TransactionId,
                Attributes = evt.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value)
            };

            _events.Add(record);
        }

        _logger.LogDebug(
            "Stored batch {BatchSeq} from device {DeviceId} with {EventCount} events",
            batch.BatchSeq, batch.DeviceId, batch.Events.Count);

        return Task.CompletedTask;
    }

    public Task<IEnumerable<EventRecord>> GetEventsAsync(string deviceId, DateTime from, DateTime to)
    {
        var fromTicks = new DateTimeOffset(from).ToUnixTimeMilliseconds();
        var toTicks = new DateTimeOffset(to).ToUnixTimeMilliseconds();

        var results = _events
            .Where(e => e.DeviceId == deviceId &&
                       e.ReceivedTimestampUtc >= fromTicks &&
                       e.ReceivedTimestampUtc <= toTicks)
            .OrderBy(e => e.ReceivedTimestampUtc)
            .AsEnumerable();

        return Task.FromResult(results);
    }

    public Task<long> GetEventCountAsync(string deviceId)
    {
        var count = _events.Count(e => e.DeviceId == deviceId);
        return Task.FromResult((long)count);
    }

    public long GetTotalEventCount() => _events.Count;
}
