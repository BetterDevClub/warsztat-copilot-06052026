using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.ValueObjects;

/// <summary>
/// Slug constrained to the rules of <see cref="Slug"/> plus a reserved-word blocklist
/// for values that would collide with platform subdomains ("www", "api", "admin" ...).
/// </summary>
public sealed class TenantSlug : ValueObject
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "app", "mail", "ftp", "blog", "docs", "status",
        "support", "help", "billing", "auth", "login", "signup", "public", "static",
        "assets", "cdn", "dev", "test", "staging", "prod", "production", "internal",
    };

    private TenantSlug(string value) => Value = value;

    /// <summary>The normalised tenant slug.</summary>
    public string Value { get; }

    /// <summary>Creates a <see cref="TenantSlug"/> from a raw string.</summary>
    public static Result<TenantSlug> Create(string? raw)
    {
        var slug = Slug.Create(raw);
        if (slug.IsFailure)
        {
            return Result.Failure<TenantSlug>(slug.Error);
        }

        if (Reserved.Contains(slug.Value.Value))
        {
            return Result.Failure<TenantSlug>(DomainErrors.SlugErrors.Reserved);
        }

        return new TenantSlug(slug.Value.Value);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
