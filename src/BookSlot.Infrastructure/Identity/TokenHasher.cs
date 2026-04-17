using System.Security.Cryptography;
using System.Text;

namespace BookSlot.Infrastructure.Identity;

/// <summary>
/// Hash helpers for opaque tokens. SHA-256 for refresh tokens (only the server ever
/// compares the hash), HMAC-SHA256 with a server pepper for API key secrets.
/// </summary>
public static class TokenHasher
{
    /// <summary>Hashes an opaque refresh token with SHA-256 and hex-encodes the result.</summary>
    public static string HashRefreshToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>HMAC-SHA256 with the configured pepper, hex-encoded. Used for API key secrets.</summary>
    public static string HmacApiKey(string secretSegment, string pepper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretSegment);
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);
        var key = Encoding.UTF8.GetBytes(pepper);
        var data = Encoding.UTF8.GetBytes(secretSegment);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash);
    }

    /// <summary>Cryptographically random URL-safe token of the given byte length (default 32).</summary>
    public static string NewOpaqueToken(int bytes = 32)
    {
        Span<byte> buffer = stackalloc byte[bytes];
        RandomNumberGenerator.Fill(buffer);
        // Base64Url without padding.
        return Convert.ToBase64String(buffer).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
