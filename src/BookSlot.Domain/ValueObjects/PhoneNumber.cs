using System.Text.RegularExpressions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.ValueObjects;

/// <summary>
/// Phone number in E.164 format ("+" followed by 8–15 digits). We intentionally do not
/// do carrier/country-specific validation — that belongs in SMS provider integration.
/// </summary>
public sealed partial class PhoneNumber : ValueObject
{
    private PhoneNumber(string value) => Value = value;

    /// <summary>Normalised E.164 value, e.g. "+15551234567".</summary>
    public string Value { get; }

    /// <summary>Creates a <see cref="PhoneNumber"/> from a raw string.</summary>
    public static Result<PhoneNumber> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Failure<PhoneNumber>(DomainErrors.PhoneErrors.Empty);
        }

        // Strip spaces, hyphens, parentheses — but keep leading +.
        var condensed = StripChars().Replace(raw.Trim(), string.Empty);

        if (!E164Regex().IsMatch(condensed))
        {
            return Result.Failure<PhoneNumber>(DomainErrors.PhoneErrors.Invalid);
        }

        return new PhoneNumber(condensed);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164Regex();

    [GeneratedRegex(@"[\s\-()]", RegexOptions.CultureInvariant)]
    private static partial Regex StripChars();
}
