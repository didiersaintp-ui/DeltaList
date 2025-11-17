namespace GrpcBackend.Services;

/// <summary>
/// Interface for rate limiting device operations
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Check if a device is allowed to send a batch
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <returns>True if allowed, false if rate limit exceeded</returns>
    bool AllowBatch(string deviceId);

    /// <summary>
    /// Get remaining quota for a device
    /// </summary>
    int GetRemainingQuota(string deviceId);

    /// <summary>
    /// Reset rate limit for a device
    /// </summary>
    void Reset(string deviceId);
}
