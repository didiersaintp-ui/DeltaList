using DeltaList.Shared.Services;
using DeltaList.Shared.Messages;
using DeltaList.Shared.Security;
using DeltaList.Shared.Interfaces;
using DeltaList.Shared.Metrics;
using DeltaList.Shared.Configuration;
using Grpc.Core;

namespace GrpcBackend.Services;

/// <summary>
/// gRPC service implementation for device authentication
/// Handles device registration and JWT token management
/// </summary>
public class DeviceAuthServiceImpl : DeviceAuthService.DeviceAuthServiceBase
{
    private readonly ILogger<DeviceAuthServiceImpl> _logger;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly MetricsCollector _metrics;
    private readonly BackendSettings _settings;

    public DeviceAuthServiceImpl(
        ILogger<DeviceAuthServiceImpl> logger,
        IDeviceRegistry deviceRegistry,
        IJwtTokenService jwtTokenService,
        MetricsCollector metrics,
        BackendSettings settings)
    {
        _logger = logger;
        _deviceRegistry = deviceRegistry;
        _jwtTokenService = jwtTokenService;
        _metrics = metrics;
        _settings = settings;
    }

    /// <summary>
    /// Register a new device and generate JWT token
    /// </summary>
    public override async Task<AuthToken> RegisterDevice(
        DeviceRegistration request,
        ServerCallContext context)
    {
        try
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(request.DeviceId))
            {
                _logger.LogWarning("Device registration failed: missing device ID");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Device ID is required"));
            }

            _logger.LogInformation("Registering device: {DeviceId}", request.DeviceId);

            // Convert metadata from proto to dictionary
            var metadata = request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value);

            // Register device in registry
            var deviceInfo = await _deviceRegistry.RegisterDeviceAsync(
                request.DeviceId,
                request.PublicKey,
                metadata);

            // Generate JWT token
            var expirationMinutes = _settings.Security.JwtExpirationMinutes;
            var token = _jwtTokenService.GenerateToken(request.DeviceId, expirationMinutes);
            var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

            _metrics.RecordConnection(); // Track device registration as connection event

            _logger.LogInformation(
                "Device {DeviceId} registered successfully, token expires at {ExpiresAt}",
                request.DeviceId,
                expiresAt);

            return new AuthToken
            {
                DeviceId = request.DeviceId,
                Token = token,
                ExpiresAtUtc = new DateTimeOffset(expiresAt).ToUnixTimeMilliseconds()
            };
        }
        catch (RpcException)
        {
            throw; // Re-throw gRPC exceptions as-is
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device {DeviceId}", request.DeviceId);
            _metrics.RecordError("device_registration_error");
            throw new RpcException(new Status(StatusCode.Internal, $"Registration failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Refresh an existing JWT token
    /// </summary>
    public override async Task<AuthToken> RefreshToken(
        AuthToken request,
        ServerCallContext context)
    {
        try
        {
            // Validate the existing token
            var deviceId = _jwtTokenService.ValidateToken(request.Token);

            if (string.IsNullOrEmpty(deviceId))
            {
                _logger.LogWarning("Token refresh failed: invalid token for device {DeviceId}", request.DeviceId);
                _metrics.RecordError("invalid_token_refresh");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or expired token"));
            }

            // Verify device ID matches
            if (deviceId != request.DeviceId)
            {
                _logger.LogWarning(
                    "Token refresh failed: device ID mismatch. Token: {TokenDeviceId}, Request: {RequestDeviceId}",
                    deviceId,
                    request.DeviceId);
                _metrics.RecordError("device_id_mismatch");
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Device ID mismatch"));
            }

            // Check if device is still registered and active
            var device = await _deviceRegistry.GetDeviceAsync(deviceId);
            if (device == null)
            {
                _logger.LogWarning("Token refresh failed: device {DeviceId} not found", deviceId);
                throw new RpcException(new Status(StatusCode.NotFound, "Device not registered"));
            }

            if (device.Status != "active")
            {
                _logger.LogWarning(
                    "Token refresh failed: device {DeviceId} has status {Status}",
                    deviceId,
                    device.Status);
                throw new RpcException(new Status(StatusCode.PermissionDenied, $"Device status: {device.Status}"));
            }

            // Update last seen timestamp
            await _deviceRegistry.UpdateLastSeenAsync(deviceId);

            // Generate new token
            var expirationMinutes = _settings.Security.JwtExpirationMinutes;
            var newToken = _jwtTokenService.GenerateToken(deviceId, expirationMinutes);
            var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

            _logger.LogInformation(
                "Token refreshed for device {DeviceId}, expires at {ExpiresAt}",
                deviceId,
                expiresAt);

            return new AuthToken
            {
                DeviceId = deviceId,
                Token = newToken,
                ExpiresAtUtc = new DateTimeOffset(expiresAt).ToUnixTimeMilliseconds()
            };
        }
        catch (RpcException)
        {
            throw; // Re-throw gRPC exceptions as-is
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token for device {DeviceId}", request.DeviceId);
            _metrics.RecordError("token_refresh_error");
            throw new RpcException(new Status(StatusCode.Internal, $"Token refresh failed: {ex.Message}"));
        }
    }
}
