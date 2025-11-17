using DeltaList.Shared.Models;

namespace DeltaList.Shared.Interfaces;

/// <summary>
/// Interface for device registration and lifecycle management
/// Supports 30k concurrent devices with sharding
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>
    /// Register a new device
    /// </summary>
    /// <param name="deviceId">Unique device identifier</param>
    /// <param name="publicKey">Device public key for authentication</param>
    /// <param name="metadata">Additional device metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Registered device information</returns>
    Task<DeviceInfo> RegisterDeviceAsync(
        string deviceId,
        string publicKey,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get device information
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Device information or null if not found</returns>
    Task<DeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate if a device is registered and active
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if device is valid and active, false otherwise</returns>
    Task<bool> ValidateDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update device status
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="status">New status (active, suspended, revoked)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update successful, false if device not found</returns>
    Task<bool> UpdateDeviceStatusAsync(
        string deviceId,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke a device (permanently disable)
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="reason">Reason for revocation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if revocation successful</returns>
    Task<bool> RevokeDeviceAsync(
        string deviceId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update device last seen timestamp
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update successful</returns>
    Task<bool> UpdateLastSeenAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all devices in a specific shard
    /// </summary>
    /// <param name="shardId">Shard identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of devices in the shard</returns>
    Task<IEnumerable<DeviceInfo>> GetDevicesByShardAsync(
        int shardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get total registered device count
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total number of registered devices</returns>
    Task<int> GetDeviceCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active device count (status = active, seen in last N minutes)
    /// </summary>
    /// <param name="lastSeenMinutes">Consider active if seen within this many minutes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of active devices</returns>
    Task<int> GetActiveDeviceCountAsync(
        int lastSeenMinutes = 5,
        CancellationToken cancellationToken = default);
}
