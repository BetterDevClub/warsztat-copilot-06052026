using System.Text.RegularExpressions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.ValueObjects;

/// <summary>
/// URL-safe slug: lowercase letters, digits and hyphens. Must start with a letter,
/// 3–64 chars, no consecutive or trailing hyphens. Used for tenant subdomains,
/// service URLs etc.
/// </summary>
public sealed partial class Slug : ValueObject
{
    /// <summary>Minimum slug length.</summary>
    public const int MinLength = 3;

    /// <summary>Maximum slug length.</summary>
    public const int MaxLength = 64;

    private Slug(string value) => Value = value;

    /// <summary>The normalised slug.</summary>
    public string Value { get; }

    /// <summary>Creates a <see cref="Slug"/> from a raw string.</summary>
    public static Result<Slug> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Failure<Slug>(DomainErrors.SlugErrors.Empty);
        }

        var normalised = raw.Trim().ToLowerInvariant();

        if (normalised.Length < MinLength)
        {
            return Result.Failure<Slug>(DomainErrors.SlugErrors.TooShort);
        }

        if (normalised.Length > MaxLength)
        {
            return Result.Failure<Slug>(DomainErrors.SlugErrors.TooLong);
        }

        if (!SlugRegex().IsMatch(normalised))
        {
            return Result.Failure<Slug>(DomainErrors.SlugErrors.Invalid);
        }

        return new Slug(normalised);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();
}
