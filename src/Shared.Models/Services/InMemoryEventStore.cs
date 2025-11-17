using DeltaList.Shared.Models;
using DeltaList.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DeltaList.Shared.Services;

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

    public Task<int> StoreEventsAsync(string deviceId, IEnumerable<EventRecord> events, CancellationToken cancellationToken = default)
    {
        var eventList = events.ToList();

        foreach (var evt in eventList)
        {
            _events.Add(evt);
        }

        _logger.LogDebug(
            "Stored {EventCount} events from device {DeviceId}",
            eventList.Count, deviceId);

        return Task.FromResult(eventList.Count);
    }

    public Task<IEnumerable<EventRecord>> GetEventsAsync(string deviceId, long fromUtc, long toUtc, CancellationToken cancellationToken = default)
    {
        var results = _events
            .Where(e => e.DeviceId == deviceId &&
                       e.ReceivedTimestampUtc >= fromUtc &&
                       e.ReceivedTimestampUtc <= toUtc)
            .OrderBy(e => e.ReceivedTimestampUtc)
            .AsEnumerable();

        return Task.FromResult(results);
    }

    public Task<long> GetEventCountAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var count = _events.Count(e => e.DeviceId == deviceId);
        return Task.FromResult((long)count);
    }

    public Task<IEnumerable<EventRecord>> GetEventsByTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var results = _events
            .Where(e => e.TransactionId == transactionId)
            .OrderBy(e => e.ReceivedTimestampUtc)
            .AsEnumerable();

        return Task.FromResult(results);
    }

    public Task<long> DeleteOldEventsAsync(long beforeUtc, CancellationToken cancellationToken = default)
    {
        // Note: ConcurrentBag doesn't support removal, so this is a limitation
        // In production, use a proper database with delete support
        var toDelete = _events.Where(e => e.ReceivedTimestampUtc < beforeUtc).ToList();

        _logger.LogWarning(
            "InMemoryEventStore does not support deletion. Found {Count} events to delete but operation is not supported.",
            toDelete.Count);

        return Task.FromResult(0L);
    }

    /// <summary>
    /// Helper method for backward compatibility - converts Batch to EventRecords
    /// </summary>
    public Task<int> StoreBatchAsync(Messages.Batch batch, DateTime receivedAt)
    {
        var receivedTicks = new DateTimeOffset(receivedAt).ToUnixTimeMilliseconds();

        var eventRecords = batch.Events.Select(evt => new EventRecord
        {
            Id = Guid.NewGuid().ToString(),
            DeviceId = evt.DeviceId,
            DeviceTimestampUtc = evt.DeviceTimestampUtc,
            ReceivedTimestampUtc = receivedTicks,
            BatchSeq = batch.BatchSeq,
            EventType = evt.EventType,
            TransactionId = evt.TransactionId,
            Attributes = evt.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value)
        }).ToList();

        return StoreEventsAsync(batch.DeviceId, eventRecords);
    }

    public long GetTotalEventCount() => _events.Count;
}
