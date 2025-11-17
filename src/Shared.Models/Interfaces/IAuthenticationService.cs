namespace DeltaList.Shared.Interfaces;

/// <summary>
/// Interface for device authentication and JWT token management
/// Supports 30k concurrent device authentication
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Authenticate a device and generate JWT token
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="publicKey">Device public key for verification</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result with JWT token</returns>
    Task<AuthenticationResult> AuthenticateDeviceAsync(
        string deviceId,
        string publicKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate an existing JWT token
    /// </summary>
    /// <param name="token">JWT token to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with device ID if valid</returns>
    Task<TokenValidationResult> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh a JWT token before it expires
    /// </summary>
    /// <param name="token">Existing valid token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New authentication result with refreshed token</returns>
    Task<AuthenticationResult> RefreshTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke a device's authentication tokens
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if revocation successful</returns>
    Task<bool> RevokeDeviceTokensAsync(
        string deviceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of device authentication
/// </summary>
public class AuthenticationResult
{
    public bool Success { get; set; }
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public long ExpiresAtUtc { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Result of token validation
/// </summary>
public class TokenValidationResult
{
    public bool IsValid { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public long ExpiresAtUtc { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
