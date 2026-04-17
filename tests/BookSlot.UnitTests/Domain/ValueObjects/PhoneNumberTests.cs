using BookSlot.Domain.Primitives;
using BookSlot.Domain.ValueObjects;

namespace BookSlot.UnitTests.Domain.ValueObjects;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+15551234567", "+15551234567")]
    [InlineData("+1 (555) 123-4567", "+15551234567")]
    [InlineData("  +48 600 700 800 ", "+48600700800")]
    public void Create_valid_e164_succeeds(string raw, string expected)
    {
        var result = PhoneNumber.Create(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_empty_fails(string? raw)
    {
        var result = PhoneNumber.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.PhoneErrors.Empty);
    }

    [Theory]
    [InlineData("5551234567")]           // missing +
    [InlineData("+0123456789")]          // leading 0 after +
    [InlineData("+1234")]                // too short (<8 digits)
    [InlineData("+1234567890123456")]    // too long (>15 digits)
    [InlineData("+1-abc-defg")]          // letters
    public void Create_invalid_format_fails(string raw)
    {
        var result = PhoneNumber.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.PhoneErrors.Invalid);
    }
}
