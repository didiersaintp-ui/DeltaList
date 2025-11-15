using System.Security.Cryptography;
using System.Text;

namespace DeltaList.Shared.Security;

/// <summary>
/// PCI-compliant PAN tokenization using HMAC-SHA256
/// Never stores or transmits raw PAN data
/// </summary>
public class PanTokenizer
{
    private readonly byte[] _secretKey;
    private readonly byte[] _salt;

    public PanTokenizer(string secretKey, string salt)
    {
        _secretKey = Encoding.UTF8.GetBytes(secretKey);
        _salt = Encoding.UTF8.GetBytes(salt);
    }

    /// <summary>
    /// Create a secure token from PAN that can be safely stored and transmitted
    /// </summary>
    /// <param name="pan">Primary Account Number (PAN)</param>
    /// <returns>Base64-encoded HMAC token</returns>
    public string TokenizePan(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan))
            throw new ArgumentException("PAN cannot be empty", nameof(pan));

        // Combine PAN with salt
        var data = Encoding.UTF8.GetBytes(pan + Convert.ToBase64String(_salt));

        // Compute HMAC-SHA256
        using var hmac = new HMACSHA256(_secretKey);
        var hash = hmac.ComputeHash(data);

        // Return base64-encoded token
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Verify if a PAN matches a token
    /// </summary>
    public bool VerifyToken(string pan, string token)
    {
        var computedToken = TokenizePan(pan);
        return string.Equals(computedToken, token, StringComparison.Ordinal);
    }

    /// <summary>
    /// Create signature for message integrity
    /// </summary>
    public string SignMessage(string message)
    {
        var data = Encoding.UTF8.GetBytes(message);
        using var hmac = new HMACSHA256(_secretKey);
        var hash = hmac.ComputeHash(data);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Verify message signature
    /// </summary>
    public bool VerifySignature(string message, string signature)
    {
        var computedSignature = SignMessage(message);
        return string.Equals(computedSignature, signature, StringComparison.Ordinal);
    }
}
