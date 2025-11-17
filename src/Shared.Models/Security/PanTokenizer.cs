using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DeltaList.Shared.Security;

/// <summary>
/// PCI-compliant PAN tokenization using HMAC-SHA256
/// Thread-safe implementation for high-concurrency scenarios (30k devices)
/// Never stores or transmits raw PAN data
/// </summary>
public partial class PanTokenizer
{
    private readonly byte[] _secretKey;
    private readonly byte[] _salt;
    private readonly object _lock = new();

    // Regex for basic PAN validation (13-19 digits)
    [GeneratedRegex(@"^\d{13,19}$")]
    private static partial Regex PanValidationRegex();

    public PanTokenizer(string secretKey, string salt)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new ArgumentException("Secret key cannot be empty", nameof(secretKey));

        if (string.IsNullOrWhiteSpace(salt))
            throw new ArgumentException("Salt cannot be empty", nameof(salt));

        if (secretKey.Length < 32)
            throw new ArgumentException("Secret key must be at least 32 characters", nameof(secretKey));

        _secretKey = Encoding.UTF8.GetBytes(secretKey);
        _salt = Encoding.UTF8.GetBytes(salt);
    }

    /// <summary>
    /// Validate PAN format (13-19 digits, optional Luhn check)
    /// </summary>
    /// <param name="pan">Primary Account Number</param>
    /// <param name="performLuhnCheck">If true, validates using Luhn algorithm</param>
    /// <returns>True if valid, false otherwise</returns>
    public bool ValidatePan(string pan, bool performLuhnCheck = false)
    {
        if (string.IsNullOrWhiteSpace(pan))
            return false;

        // Remove any spaces or dashes
        pan = pan.Replace(" ", "").Replace("-", "");

        // Check basic format (13-19 digits)
        if (!PanValidationRegex().IsMatch(pan))
            return false;

        // Perform Luhn check if requested
        if (performLuhnCheck)
        {
            return PerformLuhnCheck(pan);
        }

        return true;
    }

    /// <summary>
    /// Luhn algorithm validation (mod-10 checksum)
    /// </summary>
    private bool PerformLuhnCheck(string pan)
    {
        var sum = 0;
        var alternate = false;

        for (var i = pan.Length - 1; i >= 0; i--)
        {
            var digit = pan[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    /// <summary>
    /// Create a secure token from PAN that can be safely stored and transmitted
    /// Thread-safe implementation
    /// </summary>
    /// <param name="pan">Primary Account Number (PAN)</param>
    /// <param name="validate">If true, validates PAN format before tokenization</param>
    /// <returns>Base64-encoded HMAC token</returns>
    public string TokenizePan(string pan, bool validate = true)
    {
        if (string.IsNullOrWhiteSpace(pan))
            throw new ArgumentException("PAN cannot be empty", nameof(pan));

        // Remove any spaces or dashes
        pan = pan.Replace(" ", "").Replace("-", "");

        if (validate && !ValidatePan(pan))
            throw new ArgumentException("Invalid PAN format", nameof(pan));

        // Thread-safe tokenization
        lock (_lock)
        {
            // Combine PAN with salt
            var data = Encoding.UTF8.GetBytes(pan + Convert.ToBase64String(_salt));

            // Compute HMAC-SHA256
            using var hmac = new HMACSHA256(_secretKey);
            var hash = hmac.ComputeHash(data);

            // Return base64-encoded token
            return Convert.ToBase64String(hash);
        }
    }

    /// <summary>
    /// Verify if a PAN matches a token
    /// Thread-safe implementation with timing-attack resistance
    /// </summary>
    public bool VerifyToken(string pan, string token)
    {
        if (string.IsNullOrWhiteSpace(pan) || string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var computedToken = TokenizePan(pan, validate: false);

            // Use constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedToken),
                Encoding.UTF8.GetBytes(token));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Create signature for message integrity
    /// Thread-safe implementation
    /// </summary>
    public string SignMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be empty", nameof(message));

        lock (_lock)
        {
            var data = Encoding.UTF8.GetBytes(message);
            using var hmac = new HMACSHA256(_secretKey);
            var hash = hmac.ComputeHash(data);
            return Convert.ToBase64String(hash);
        }
    }

    /// <summary>
    /// Verify message signature
    /// Thread-safe implementation with timing-attack resistance
    /// </summary>
    public bool VerifySignature(string message, string signature)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(signature))
            return false;

        try
        {
            var computedSignature = SignMessage(message);

            // Use constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(signature));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Mask PAN for logging (show first 6 and last 4 digits)
    /// </summary>
    /// <param name="pan">Primary Account Number</param>
    /// <returns>Masked PAN (e.g., "123456******7890")</returns>
    public static string MaskPan(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan))
            return string.Empty;

        pan = pan.Replace(" ", "").Replace("-", "");

        if (pan.Length < 10)
            return new string('*', pan.Length);

        var first6 = pan.Substring(0, 6);
        var last4 = pan.Substring(pan.Length - 4);
        var masked = new string('*', pan.Length - 10);

        return $"{first6}{masked}{last4}";
    }
}
