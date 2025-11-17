using DeltaList.Shared.Configuration;
using DeltaList.Shared.Security;
using DeltaList.Shared.Metrics;
using Microsoft.Extensions.Caching.Distributed;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace MqttBackend.Services;

/// <summary>
/// Service for authenticating MQTT device connections
/// Supports JWT tokens and username/password authentication
/// </summary>
public class MqttAuthenticationService
{
    private readonly ILogger<MqttAuthenticationService> _logger;
    private readonly BackendSettings _settings;
    private readonly IDistributedCache _cache;
    private readonly MetricsCollector _metrics;

    private const string DEVICE_CACHE_PREFIX = "device:";
    private const string REVOKED_TOKENS_KEY = "auth:revoked_tokens";

    public MqttAuthenticationService(
        ILogger<MqttAuthenticationService> logger,
        BackendSettings settings,
        IDistributedCache cache,
        MetricsCollector metrics)
    {
        _logger = logger;
        _settings = settings;
        _cache = cache;
        _metrics = metrics;
    }

    /// <summary>
    /// Authenticate device using username and password
    /// Username format: "device-{deviceId}" or JWT token
    /// </summary>
    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password)
    {
        try
        {
            // Try JWT authentication first
            if (IsJwtToken(username))
            {
                return await AuthenticateWithJwtAsync(username);
            }

            // Username/password authentication
            return await AuthenticateWithCredentialsAsync(username, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error for user {Username}", username);
            _metrics.RecordError("auth_error");
            return AuthenticationResult.Failed("Authentication error");
        }
    }

    /// <summary>
    /// Authenticate using JWT token
    /// </summary>
    private async Task<AuthenticationResult> AuthenticateWithJwtAsync(string token)
    {
        try
        {
            // Check if token is revoked
            if (await IsTokenRevokedAsync(token))
            {
                _logger.LogWarning("Attempt to use revoked token");
                return AuthenticationResult.Failed("Token revoked");
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_settings.Security.JwtSecretKey ?? _settings.Security.SecretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _settings.Security.JwtIssuer,
                ValidAudience = _settings.Security.JwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

            var deviceId = principal.FindFirst("deviceId")?.Value
                          ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(deviceId))
            {
                _logger.LogWarning("JWT token missing deviceId claim");
                return AuthenticationResult.Failed("Invalid token claims");
            }

            // Check if device is active
            var isActive = await IsDeviceActiveAsync(deviceId);
            if (!isActive)
            {
                _logger.LogWarning("Device {DeviceId} is not active", deviceId);
                return AuthenticationResult.Failed("Device not active");
            }

            _logger.LogInformation("JWT authentication successful for device {DeviceId}", deviceId);
            _metrics.RecordConnection();

            return AuthenticationResult.Success(deviceId);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Invalid JWT token");
            return AuthenticationResult.Failed("Invalid token");
        }
    }

    /// <summary>
    /// Authenticate using username/password credentials
    /// </summary>
    private async Task<AuthenticationResult> AuthenticateWithCredentialsAsync(string username, string password)
    {
        // Extract deviceId from username format: "device-{deviceId}"
        if (!username.StartsWith("device-"))
        {
            _logger.LogWarning("Invalid username format: {Username}", username);
            return AuthenticationResult.Failed("Invalid username format");
        }

        var deviceId = username.Substring(7); // Remove "device-" prefix

        // Load device credentials from cache
        var deviceKey = DEVICE_CACHE_PREFIX + deviceId;
        var deviceJson = await _cache.GetStringAsync(deviceKey);

        if (string.IsNullOrEmpty(deviceJson))
        {
            _logger.LogWarning("Device {DeviceId} not found", deviceId);
            return AuthenticationResult.Failed("Device not found");
        }

        var device = JsonSerializer.Deserialize<DeviceCredentials>(deviceJson);
        if (device == null)
        {
            _logger.LogError("Failed to deserialize device credentials for {DeviceId}", deviceId);
            return AuthenticationResult.Failed("Invalid device data");
        }

        // Verify password
        if (device.PasswordHash != ComputePasswordHash(password, device.Salt))
        {
            _logger.LogWarning("Invalid password for device {DeviceId}", deviceId);
            _metrics.RecordError("auth_invalid_password");
            return AuthenticationResult.Failed("Invalid password");
        }

        // Check if device is active
        if (!device.IsActive)
        {
            _logger.LogWarning("Device {DeviceId} is not active", deviceId);
            return AuthenticationResult.Failed("Device not active");
        }

        _logger.LogInformation("Credential authentication successful for device {DeviceId}", deviceId);
        _metrics.RecordConnection();

        return AuthenticationResult.Success(deviceId);
    }

    /// <summary>
    /// Register a new device with credentials
    /// </summary>
    public async Task<string> RegisterDeviceAsync(string deviceId, string password)
    {
        var salt = GenerateSalt();
        var passwordHash = ComputePasswordHash(password, salt);

        var device = new DeviceCredentials
        {
            DeviceId = deviceId,
            PasswordHash = passwordHash,
            Salt = salt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastAuthAt = null
        };

        var deviceKey = DEVICE_CACHE_PREFIX + deviceId;
        var deviceJson = JsonSerializer.Serialize(device);
        await _cache.SetStringAsync(deviceKey, deviceJson);

        _logger.LogInformation("Registered new device {DeviceId}", deviceId);

        return deviceId;
    }

    /// <summary>
    /// Revoke device access
    /// </summary>
    public async Task RevokeDeviceAsync(string deviceId)
    {
        var deviceKey = DEVICE_CACHE_PREFIX + deviceId;
        var deviceJson = await _cache.GetStringAsync(deviceKey);

        if (!string.IsNullOrEmpty(deviceJson))
        {
            var device = JsonSerializer.Deserialize<DeviceCredentials>(deviceJson);
            if (device != null)
            {
                device.IsActive = false;
                deviceJson = JsonSerializer.Serialize(device);
                await _cache.SetStringAsync(deviceKey, deviceJson);

                _logger.LogInformation("Revoked device {DeviceId}", deviceId);
            }
        }
    }

    /// <summary>
    /// Check if device is active
    /// </summary>
    private async Task<bool> IsDeviceActiveAsync(string deviceId)
    {
        var deviceKey = DEVICE_CACHE_PREFIX + deviceId;
        var deviceJson = await _cache.GetStringAsync(deviceKey);

        if (string.IsNullOrEmpty(deviceJson))
            return false;

        var device = JsonSerializer.Deserialize<DeviceCredentials>(deviceJson);
        return device?.IsActive ?? false;
    }

    /// <summary>
    /// Check if JWT token is revoked
    /// </summary>
    private async Task<bool> IsTokenRevokedAsync(string token)
    {
        var revokedTokensJson = await _cache.GetStringAsync(REVOKED_TOKENS_KEY);
        if (string.IsNullOrEmpty(revokedTokensJson))
            return false;

        var revokedTokens = JsonSerializer.Deserialize<HashSet<string>>(revokedTokensJson);
        return revokedTokens?.Contains(token) ?? false;
    }

    /// <summary>
    /// Check if string is a JWT token
    /// </summary>
    private static bool IsJwtToken(string value)
    {
        return value.Contains('.') && value.Split('.').Length == 3;
    }

    /// <summary>
    /// Compute password hash using HMAC-SHA256
    /// </summary>
    private string ComputePasswordHash(string password, string salt)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(salt));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Generate random salt
    /// </summary>
    private static string GenerateSalt()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}

/// <summary>
/// Device credentials stored in cache
/// </summary>
public class DeviceCredentials
{
    public string DeviceId { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAuthAt { get; set; }
}

/// <summary>
/// Authentication result
/// </summary>
public class AuthenticationResult
{
    public bool IsAuthenticated { get; set; }
    public string? DeviceId { get; set; }
    public string? ErrorMessage { get; set; }

    public static AuthenticationResult Success(string deviceId)
    {
        return new AuthenticationResult
        {
            IsAuthenticated = true,
            DeviceId = deviceId
        };
    }

    public static AuthenticationResult Failed(string errorMessage)
    {
        return new AuthenticationResult
        {
            IsAuthenticated = false,
            ErrorMessage = errorMessage
        };
    }
}
