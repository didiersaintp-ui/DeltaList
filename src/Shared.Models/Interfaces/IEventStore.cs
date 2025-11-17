using DeltaList.Shared.Models;

namespace DeltaList.Shared.Interfaces;

/// <summary>
/// Interface for storing and retrieving device events
/// Supports high-throughput event ingestion for 30k devices
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Store a batch of events from a device
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="events">List of events to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of events successfully stored</returns>
    Task<int> StoreEventsAsync(string deviceId, IEnumerable<EventRecord> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve events for a specific device within a time range
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="fromUtc">Start timestamp (Unix milliseconds)</param>
    /// <param name="toUtc">End timestamp (Unix milliseconds)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of event records</returns>
    Task<IEnumerable<EventRecord>> GetEventsAsync(string deviceId, long fromUtc, long toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get total event count for a device
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total number of events</returns>
    Task<long> GetEventCountAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get events by transaction ID for tracing
    /// </summary>
    /// <param name="transactionId">Transaction identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of event records</returns>
    Task<IEnumerable<EventRecord>> GetEventsByTransactionAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete events older than specified timestamp
    /// </summary>
    /// <param name="beforeUtc">Delete events before this timestamp (Unix milliseconds)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of events deleted</returns>
    Task<long> DeleteOldEventsAsync(long beforeUtc, CancellationToken cancellationToken = default);
}
