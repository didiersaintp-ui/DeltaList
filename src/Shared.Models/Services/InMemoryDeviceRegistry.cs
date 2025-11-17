using DeltaList.Shared.Interfaces;
using DeltaList.Shared.Models;
using System.Collections.Concurrent;

namespace DeltaList.Shared.Services;

/// <summary>
/// In-memory implementation of device registry for PoC
/// For production, replace with database-backed implementation
/// </summary>
public class InMemoryDeviceRegistry : IDeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceInfo> _devices = new();
    private int _nextShardId = 0;

    public Task<DeviceInfo> RegisterDeviceAsync(
        string deviceId,
        string publicKey,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var device = _devices.AddOrUpdate(
            deviceId,
            // Add new device
            _ => new DeviceInfo
            {
                DeviceId = deviceId,
                PublicKey = publicKey,
                RegisteredAt = now,
                LastSeenAt = now,
                Status = "active",
                Metadata = metadata ?? new Dictionary<string, string>(),
                ShardId = Interlocked.Increment(ref _nextShardId) % 10 // Simple sharding
            },
            // Update existing device
            (_, existing) =>
            {
                existing.PublicKey = publicKey;
                existing.LastSeenAt = now;
                if (metadata != null)
                {
                    existing.Metadata = metadata;
                }
                return existing;
            });

        return Task.FromResult(device);
    }

    public Task<DeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _devices.TryGetValue(deviceId, out var device);
        return Task.FromResult(device);
    }

    public Task<bool> ValidateDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_devices.TryGetValue(deviceId, out var device))
        {
            return Task.FromResult(device.Status == "active");
        }
        return Task.FromResult(false);
    }

    public Task<bool> UpdateDeviceStatusAsync(
        string deviceId,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (_devices.TryGetValue(deviceId, out var device))
        {
            device.Status = status;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> UpdateLastSeenAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_devices.TryGetValue(deviceId, out var device))
        {
            device.LastSeenAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> RevokeDeviceAsync(
        string deviceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (_devices.TryGetValue(deviceId, out var device))
        {
            device.Status = "revoked";
            device.Metadata["revocation_reason"] = reason;
            device.Metadata["revoked_at"] = DateTime.UtcNow.ToString("o");
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<IEnumerable<DeviceInfo>> GetDevicesByShardAsync(
        int shardId,
        CancellationToken cancellationToken = default)
    {
        var devices = _devices.Values.Where(d => d.ShardId == shardId).ToList();
        return Task.FromResult<IEnumerable<DeviceInfo>>(devices);
    }

    public Task<int> GetDeviceCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_devices.Count);
    }

    public Task<int> GetActiveDeviceCountAsync(
        int lastSeenMinutes = 5,
        CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow.AddMinutes(-lastSeenMinutes);
        var count = _devices.Values.Count(d =>
            d.Status == "active" &&
            d.LastSeenAt >= threshold);
        return Task.FromResult(count);
    }
}
