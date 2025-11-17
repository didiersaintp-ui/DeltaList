using System.Collections.Concurrent;
using DeltaList.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeltaList.Shared.Authentication;

/// <summary>
/// Device authentication service using JWT tokens
/// Thread-safe implementation for 30k concurrent device authentication
/// </summary>
public class DeviceAuthenticationService : IAuthenticationService
{
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly JwtTokenService _jwtTokenService;
    private readonly ILogger<DeviceAuthenticationService>? _logger;

    // Thread-safe revoked tokens cache (device_id -> revocation_timestamp)
    private readonly ConcurrentDictionary<string, long> _revokedDevices;

    // Thread-safe refresh token tracking (refresh_token -> device_id)
    private readonly ConcurrentDictionary<string, string> _refreshTokens;

    public DeviceAuthenticationService(
        IDeviceRegistry deviceRegistry,
        JwtTokenService jwtTokenService,
        ILogger<DeviceAuthenticationService>? logger = null)
    {
        _deviceRegistry = deviceRegistry ?? throw new ArgumentNullException(nameof(deviceRegistry));
        _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        _logger = logger;
        _revokedDevices = new ConcurrentDictionary<string, long>();
        _refreshTokens = new ConcurrentDictionary<string, string>();
    }

    /// <summary>
    /// Authenticate a device and generate JWT tokens
    /// </summary>
    public async Task<AuthenticationResult> AuthenticateDeviceAsync(
        string deviceId,
        string publicKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Device ID is required"
            };
        }

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Public key is required"
            };
        }

        try
        {
            // Check if device is revoked
            if (_revokedDevices.ContainsKey(deviceId))
            {
                _logger?.LogWarning("Authentication denied for revoked device {DeviceId}", deviceId);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Device has been revoked"
                };
            }

            // Validate device in registry
            var device = await _deviceRegistry.GetDeviceAsync(deviceId, cancellationToken);

            if (device == null)
            {
                _logger?.LogWarning("Authentication failed: device {DeviceId} not found", deviceId);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Device not registered"
                };
            }

            // Verify device status
            if (device.Status != "active")
            {
                _logger?.LogWarning("Authentication denied for {Status} device {DeviceId}",
                    device.Status, deviceId);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = $"Device is {device.Status}"
                };
            }

            // Verify public key matches (basic verification)
            if (device.PublicKey != publicKey)
            {
                _logger?.LogWarning("Authentication failed: invalid public key for device {DeviceId}", deviceId);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Invalid credentials"
                };
            }

            // Generate tokens
            var (accessToken, accessExpiresAt) = _jwtTokenService.GenerateAccessToken(
                deviceId,
                device.ShardId,
                new Dictionary<string, string>
                {
                    { "status", device.Status }
                });

            var (refreshToken, refreshExpiresAt) = _jwtTokenService.GenerateRefreshToken(deviceId);

            // Store refresh token mapping
            _refreshTokens[refreshToken] = deviceId;

            // Update last seen timestamp
            await _deviceRegistry.UpdateLastSeenAsync(deviceId, cancellationToken);

            _logger?.LogInformation("Successfully authenticated device {DeviceId}", deviceId);

            return new AuthenticationResult
            {
                Success = true,
                Token = accessToken,
                RefreshToken = refreshToken,
                ExpiresAtUtc = accessExpiresAt
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Authentication error for device {DeviceId}", deviceId);
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Authentication failed due to internal error"
            };
        }
    }

    /// <summary>
    /// Validate a JWT access token
    /// </summary>
    public async Task<TokenValidationResult> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TokenValidationResult
            {
                IsValid = false,
                ErrorMessage = "Token is required"
            };
        }

        try
        {
            // Validate JWT token
            var jwtResult = _jwtTokenService.ValidateToken(token);

            if (!jwtResult.IsValid)
            {
                return new TokenValidationResult
                {
                    IsValid = false,
                    ErrorMessage = jwtResult.ErrorMessage
                };
            }

            var deviceId = jwtResult.DeviceId;

            // Check if device has been revoked
            if (_revokedDevices.TryGetValue(deviceId, out var revokedAt))
            {
                // Check if token was issued before revocation
                var tokenIssuedAt = jwtResult.Claims.TryGetValue("iat", out var iatStr)
                    ? long.Parse(iatStr) * 1000
                    : 0;

                if (tokenIssuedAt < revokedAt)
                {
                    _logger?.LogWarning("Token rejected for revoked device {DeviceId}", deviceId);
                    return new TokenValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Device has been revoked"
                    };
                }
            }

            // Verify device still exists and is active
            var isValid = await _deviceRegistry.ValidateDeviceAsync(deviceId, cancellationToken);

            if (!isValid)
            {
                _logger?.LogWarning("Token validation failed: device {DeviceId} is not valid", deviceId);
                return new TokenValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Device is not valid"
                };
            }

            return new TokenValidationResult
            {
                IsValid = true,
                DeviceId = deviceId,
                ExpiresAtUtc = jwtResult.ExpiresAtUtc
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Token validation error");
            return new TokenValidationResult
            {
                IsValid = false,
                ErrorMessage = "Token validation failed"
            };
        }
    }

    /// <summary>
    /// Refresh an access token using a refresh token
    /// </summary>
    public async Task<AuthenticationResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Refresh token is required"
            };
        }

        try
        {
            // Validate refresh token
            var jwtResult = _jwtTokenService.ValidateToken(refreshToken);

            if (!jwtResult.IsValid)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Invalid refresh token"
                };
            }

            var deviceId = jwtResult.DeviceId;

            // Verify refresh token is tracked
            if (!_refreshTokens.TryGetValue(refreshToken, out var trackedDeviceId) ||
                trackedDeviceId != deviceId)
            {
                _logger?.LogWarning("Refresh token not found or device mismatch for {DeviceId}", deviceId);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Invalid refresh token"
                };
            }

            // Check if device is revoked
            if (_revokedDevices.ContainsKey(deviceId))
            {
                _logger?.LogWarning("Refresh denied for revoked device {DeviceId}", deviceId);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Device has been revoked"
                };
            }

            // Get device info
            var device = await _deviceRegistry.GetDeviceAsync(deviceId, cancellationToken);

            if (device == null || device.Status != "active")
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Device is not active"
                };
            }

            // Generate new access token
            var (accessToken, accessExpiresAt) = _jwtTokenService.GenerateAccessToken(
                deviceId,
                device.ShardId,
                new Dictionary<string, string>
                {
                    { "status", device.Status }
                });

            // Update last seen
            await _deviceRegistry.UpdateLastSeenAsync(deviceId, cancellationToken);

            _logger?.LogInformation("Successfully refreshed token for device {DeviceId}", deviceId);

            return new AuthenticationResult
            {
                Success = true,
                Token = accessToken,
                RefreshToken = refreshToken, // Keep same refresh token
                ExpiresAtUtc = accessExpiresAt
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Token refresh error");
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Token refresh failed"
            };
        }
    }

    /// <summary>
    /// Revoke all tokens for a device
    /// </summary>
    public async Task<bool> RevokeDeviceTokensAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        try
        {
            // Add to revoked devices with current timestamp
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _revokedDevices[deviceId] = now;

            // Remove all refresh tokens for this device
            var tokensToRemove = _refreshTokens
                .Where(kvp => kvp.Value == deviceId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in tokensToRemove)
            {
                _refreshTokens.TryRemove(token, out _);
            }

            // Update device status in registry
            await _deviceRegistry.UpdateDeviceStatusAsync(deviceId, "revoked", cancellationToken);

            _logger?.LogInformation("Revoked all tokens for device {DeviceId}", deviceId);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error revoking tokens for device {DeviceId}", deviceId);
            return false;
        }
    }

    /// <summary>
    /// Clean up expired refresh tokens (should be called periodically)
    /// </summary>
    public void CleanupExpiredTokens()
    {
        var expiredTokens = _refreshTokens.Keys
            .Where(token => _jwtTokenService.IsTokenExpired(token))
            .ToList();

        foreach (var token in expiredTokens)
        {
            _refreshTokens.TryRemove(token, out _);
        }

        if (expiredTokens.Count > 0)
        {
            _logger?.LogInformation("Cleaned up {Count} expired refresh tokens", expiredTokens.Count);
        }
    }

    /// <summary>
    /// Get current statistics
    /// </summary>
    public (int activeRefreshTokens, int revokedDevices) GetStatistics()
    {
        return (_refreshTokens.Count, _revokedDevices.Count);
    }
}
