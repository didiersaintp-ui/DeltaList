namespace DeltaList.Shared.Security;

/// <summary>
/// Interface for JWT token generation and validation
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generate a JWT token for a device
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="expirationMinutes">Token expiration time in minutes</param>
    /// <returns>JWT token string</returns>
    string GenerateToken(string deviceId, int expirationMinutes = 60);

    /// <summary>
    /// Validate a JWT token and extract device ID
    /// </summary>
    /// <param name="token">JWT token to validate</param>
    /// <returns>Device ID if valid, null otherwise</returns>
    string? ValidateToken(string token);

    /// <summary>
    /// Get token expiration time
    /// </summary>
    DateTime GetTokenExpiration(string token);
}
