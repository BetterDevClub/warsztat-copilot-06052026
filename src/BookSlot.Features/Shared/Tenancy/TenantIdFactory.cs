using System.Security.Cryptography;
using System.Text;

namespace BookSlot.Features.Shared.Tenancy;

/// <summary>
/// Produces deterministic tenant ids from slugs. Used as a bridge during Phases 5–6 when
/// tenant resolution runs before the <c>Tenants</c> table exists (Phase 7). After Phase 7
/// the real lookup takes over but the factory is still useful for test fixtures.
/// </summary>
public static class TenantIdFactory
{
    // Fixed namespace GUID for BookSlot tenants so derived ids stay stable across builds.
    private static readonly byte[] NamespaceBytes = Guid.Parse("b0055107-7e4a-4a1e-9c0b-5f7c6f5a9f01").ToByteArray();

    /// <summary>
    /// Hash a slug with SHA-256 inside a fixed namespace and return the first 16 bytes as a GUID.
    /// Same slug → same GUID on every machine, every process.
    /// </summary>
    public static Guid FromSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var normalized = slug.Trim().ToLowerInvariant();
        Span<byte> buffer = stackalloc byte[NamespaceBytes.Length + Encoding.UTF8.GetMaxByteCount(normalized.Length)];
        NamespaceBytes.CopyTo(buffer);
        var slugLen = Encoding.UTF8.GetBytes(normalized, buffer[NamespaceBytes.Length..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer[..(NamespaceBytes.Length + slugLen)], hash);
        return new Guid(hash[..16]);
    }
}
