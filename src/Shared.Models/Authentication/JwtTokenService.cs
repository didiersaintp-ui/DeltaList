using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace DeltaList.Shared.Authentication;

/// <summary>
/// JWT token service for device authentication
/// Generates and validates JWT tokens with expiration and refresh capabilities
/// Thread-safe implementation for high-concurrency scenarios (30k devices)
/// </summary>
public class JwtTokenService
{
    private readonly JwtTokenConfiguration _configuration;
    private readonly ILogger<JwtTokenService>? _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    public JwtTokenService(JwtTokenConfiguration configuration, ILogger<JwtTokenService>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
        _tokenHandler = new JwtSecurityTokenHandler();

        // Initialize signing key and credentials
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.SecretKey));
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        // Configure token validation parameters
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _configuration.Issuer,
            ValidAudience = _configuration.Audience,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.FromMinutes(1) // Allow 1 minute clock skew
        };
    }

    /// <summary>
    /// Generate a new JWT access token for a device
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="shardId">Device shard ID</param>
    /// <param name="additionalClaims">Optional additional claims</param>
    /// <returns>JWT token string and expiration timestamp</returns>
    public (string token, long expiresAtUtc) GenerateAccessToken(
        string deviceId,
        int shardId,
        Dictionary<string, string>? additionalClaims = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID cannot be empty", nameof(deviceId));

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_configuration.AccessTokenExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, deviceId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("device_id", deviceId),
            new("shard_id", shardId.ToString())
        };

        // Add additional claims if provided
        if (additionalClaims != null)
        {
            foreach (var kvp in additionalClaims)
            {
                claims.Add(new Claim(kvp.Key, kvp.Value));
            }
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = _configuration.Issuer,
            Audience = _configuration.Audience,
            SigningCredentials = _signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = _tokenHandler.WriteToken(token);

        _logger?.LogDebug("Generated access token for device {DeviceId}, expires at {ExpiresAt}",
            deviceId, expiresAt);

        return (tokenString, new DateTimeOffset(expiresAt).ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Generate a refresh token for long-lived authentication
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <returns>Refresh token string and expiration timestamp</returns>
    public (string refreshToken, long expiresAtUtc) GenerateRefreshToken(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID cannot be empty", nameof(deviceId));

        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(_configuration.RefreshTokenExpirationDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, deviceId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("token_type", "refresh"),
            new("device_id", deviceId)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = _configuration.Issuer,
            Audience = _configuration.Audience,
            SigningCredentials = _signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = _tokenHandler.WriteToken(token);

        _logger?.LogDebug("Generated refresh token for device {DeviceId}, expires at {ExpiresAt}",
            deviceId, expiresAt);

        return (tokenString, new DateTimeOffset(expiresAt).ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Validate a JWT token and extract claims
    /// </summary>
    /// <param name="token">JWT token to validate</param>
    /// <returns>Validation result with device ID and claims</returns>
    public JwtValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new JwtValidationResult
            {
                IsValid = false,
                ErrorMessage = "Token is null or empty"
            };
        }

        try
        {
            var principal = _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken)
            {
                return new JwtValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Invalid token format"
                };
            }

            var deviceId = principal.FindFirst("device_id")?.Value ??
                          principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(deviceId))
            {
                return new JwtValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Device ID not found in token"
                };
            }

            var shardIdClaim = principal.FindFirst("shard_id")?.Value;
            var shardId = int.TryParse(shardIdClaim, out var parsedShardId) ? parsedShardId : 0;

            var expiresAt = jwtToken.ValidTo;

            _logger?.LogDebug("Successfully validated token for device {DeviceId}", deviceId);

            return new JwtValidationResult
            {
                IsValid = true,
                DeviceId = deviceId,
                ShardId = shardId,
                ExpiresAtUtc = new DateTimeOffset(expiresAt).ToUnixTimeMilliseconds(),
                Claims = principal.Claims.ToDictionary(c => c.Type, c => c.Value)
            };
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger?.LogWarning("Token expired: {Message}", ex.Message);
            return new JwtValidationResult
            {
                IsValid = false,
                ErrorMessage = "Token has expired"
            };
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            _logger?.LogWarning("Invalid token signature: {Message}", ex.Message);
            return new JwtValidationResult
            {
                IsValid = false,
                ErrorMessage = "Invalid token signature"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Token validation failed");
            return new JwtValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Token validation failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Extract device ID from token without full validation (useful for logging)
    /// </summary>
    /// <param name="token">JWT token</param>
    /// <returns>Device ID or null if not found</returns>
    public string? ExtractDeviceId(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var jwtToken = _tokenHandler.ReadJwtToken(token);
            return jwtToken.Claims.FirstOrDefault(c => c.Type == "device_id")?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if a token is expired without full validation
    /// </summary>
    /// <param name="token">JWT token</param>
    /// <returns>True if expired, false otherwise</returns>
    public bool IsTokenExpired(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return true;

        try
        {
            var jwtToken = _tokenHandler.ReadJwtToken(token);
            return jwtToken.ValidTo < DateTime.UtcNow;
        }
        catch
        {
            return true;
        }
    }
}

/// <summary>
/// Configuration for JWT token service
/// </summary>
public class JwtTokenConfiguration
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "DeltaList";
    public string Audience { get; set; } = "DeltaList.Devices";
    public int AccessTokenExpirationMinutes { get; set; } = 60; // 1 hour
    public int RefreshTokenExpirationDays { get; set; } = 30; // 30 days

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("JWT SecretKey is required");

        if (SecretKey.Length < 32)
            throw new InvalidOperationException("JWT SecretKey must be at least 32 characters");

        if (AccessTokenExpirationMinutes <= 0)
            throw new InvalidOperationException("AccessTokenExpirationMinutes must be positive");

        if (RefreshTokenExpirationDays <= 0)
            throw new InvalidOperationException("RefreshTokenExpirationDays must be positive");
    }
}

/// <summary>
/// Result of JWT token validation
/// </summary>
public class JwtValidationResult
{
    public bool IsValid { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public int ShardId { get; set; }
    public long ExpiresAtUtc { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public Dictionary<string, string> Claims { get; set; } = new();
}
