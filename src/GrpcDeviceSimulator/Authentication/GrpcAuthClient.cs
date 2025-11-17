using System.Net.Http.Json;
using System.Text.Json;
using Serilog;

namespace GrpcDeviceSimulator.Authentication;

/// <summary>
/// Client for device authentication via HTTP/REST
/// Simulates JWT token acquisition and refresh
/// </summary>
public class GrpcAuthClient
{
    private readonly string _authServerUrl;
    private readonly HttpClient _httpClient;
    private readonly bool _useAuthentication;

    public GrpcAuthClient(string authServerUrl, bool useAuthentication = true)
    {
        _authServerUrl = authServerUrl;
        _useAuthentication = useAuthentication;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>
    /// Register device and get JWT token
    /// </summary>
    public async Task<string?> RegisterAndGetTokenAsync(string deviceId, CancellationToken ct = default)
    {
        if (!_useAuthentication)
        {
            Log.Debug("Authentication disabled, returning null token");
            return null;
        }

        try
        {
            var payload = new { deviceId };
            var response = await _httpClient.PostAsJsonAsync(
                $"{_authServerUrl}/api/auth/register",
                payload,
                ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);
                if (result?.Token != null)
                {
                    Log.Debug("Device {DeviceId} registered successfully, token acquired", deviceId);
                    return result.Token;
                }
            }
            else
            {
                Log.Warning("Device {DeviceId} registration failed: {StatusCode}",
                    deviceId, response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Warning("Auth server unreachable for device {DeviceId}: {Message}", deviceId, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during device {DeviceId} registration", deviceId);
        }

        return null;
    }

    /// <summary>
    /// Refresh JWT token
    /// </summary>
    public async Task<string?> RefreshTokenAsync(string oldToken, CancellationToken ct = default)
    {
        if (!_useAuthentication || string.IsNullOrEmpty(oldToken))
        {
            return null;
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oldToken);

            var response = await _httpClient.PostAsync(
                $"{_authServerUrl}/api/auth/refresh",
                null,
                ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);
                if (result?.Token != null)
                {
                    Log.Debug("Token refreshed successfully");
                    return result.Token;
                }
            }
            else
            {
                Log.Warning("Token refresh failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Warning("Auth server unreachable for token refresh: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during token refresh");
        }

        return null;
    }

    /// <summary>
    /// Validate token with auth server
    /// </summary>
    public async Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        if (!_useAuthentication || string.IsNullOrEmpty(token))
        {
            return true; // No authentication required
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.GetAsync(
                $"{_authServerUrl}/api/auth/validate",
                ct);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Warning("Token validation failed: {Message}", ex.Message);
            return false;
        }
    }

    private class AuthResponse
    {
        public string? Token { get; set; }
        public long ExpiresAt { get; set; }
    }
}
