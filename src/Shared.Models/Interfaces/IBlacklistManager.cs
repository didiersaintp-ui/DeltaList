using DeltaList.Shared.Models;
using DeltaList.Shared.Messages;

namespace DeltaList.Shared.Interfaces;

/// <summary>
/// Interface for managing PAN blacklist and distributing deltas to devices
/// Supports sharding for 30k devices and efficient delta distribution
/// </summary>
public interface IBlacklistManager
{
    /// <summary>
    /// Add PAN tokens to the blacklist
    /// </summary>
    /// <param name="panTokens">Tokenized PAN identifiers to add</param>
    /// <param name="shardId">Target shard (optional, -1 for all shards)</param>
    /// <param name="reason">Reason for blacklisting</param>
    /// <param name="updatedBy">User/system that made the update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Delta sequence number and number of devices notified</returns>
    Task<(ulong seqNo, int devicesNotified)> AddToBlacklistAsync(
        IEnumerable<string> panTokens,
        int shardId,
        string reason,
        string updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove PAN tokens from the blacklist
    /// </summary>
    /// <param name="panTokens">Tokenized PAN identifiers to remove</param>
    /// <param name="shardId">Target shard (optional, -1 for all shards)</param>
    /// <param name="reason">Reason for removal</param>
    /// <param name="updatedBy">User/system that made the update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Delta sequence number and number of devices notified</returns>
    Task<(ulong seqNo, int devicesNotified)> RemoveFromBlacklistAsync(
        IEnumerable<string> panTokens,
        int shardId,
        string reason,
        string updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current blacklist state for a shard
    /// </summary>
    /// <param name="shardId">Shard identifier (-1 for all shards)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current blacklist state</returns>
    Task<BlacklistState> GetBlacklistStateAsync(int shardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get blacklist deltas since a specific sequence number
    /// Used for catch-up when device reconnects
    /// </summary>
    /// <param name="sinceSeqNo">Get deltas after this sequence number</param>
    /// <param name="shardId">Shard identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of blacklist deltas</returns>
    Task<IEnumerable<BlacklistDelta>> GetDeltasSinceAsync(
        ulong sinceSeqNo,
        int shardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a PAN token is in the blacklist
    /// </summary>
    /// <param name="panToken">Tokenized PAN identifier</param>
    /// <param name="shardId">Shard identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if blacklisted, false otherwise</returns>
    Task<bool> IsBlacklistedAsync(string panToken, int shardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get total count of blacklisted tokens for a shard
    /// </summary>
    /// <param name="shardId">Shard identifier (-1 for all shards)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Count of blacklisted tokens</returns>
    Task<int> GetBlacklistCountAsync(int shardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initialize the blacklist manager (load persisted state, etc.)
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
