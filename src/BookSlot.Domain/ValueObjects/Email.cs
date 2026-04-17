using System.Text.RegularExpressions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.ValueObjects;

/// <summary>
/// RFC-compliant-ish email address value object. Validation is intentionally pragmatic:
/// length + single "@" + non-empty local/domain parts + a dot in the domain. Matches
/// the 99% case for web signups while rejecting obvious garbage.
/// </summary>
public sealed partial class Email : ValueObject
{
    /// <summary>Maximum email length (RFC 5321 path limit).</summary>
    public const int MaxLength = 254;

    private Email(string value) => Value = value;

    /// <summary>Normalised email value (trimmed, lowercased).</summary>
    public string Value { get; }

    /// <summary>Creates an <see cref="Email"/> from a raw string.</summary>
    public static Result<Email> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Failure<Email>(DomainErrors.EmailErrors.Empty);
        }

        var normalised = raw.Trim().ToLowerInvariant();

        if (normalised.Length > MaxLength)
        {
            return Result.Failure<Email>(DomainErrors.EmailErrors.TooLong);
        }

        if (!EmailRegex().IsMatch(normalised))
        {
            return Result.Failure<Email>(DomainErrors.EmailErrors.Invalid);
        }

        return new Email(normalised);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
