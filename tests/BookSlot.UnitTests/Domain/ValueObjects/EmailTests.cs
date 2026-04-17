using BookSlot.Domain.Primitives;
using BookSlot.Domain.ValueObjects;

namespace BookSlot.UnitTests.Domain.ValueObjects;

public class EmailTests
{
    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData("  USER@Example.COM  ", "user@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk", "first.last+tag@sub.example.co.uk")]
    public void Create_valid_email_normalises_and_succeeds(string raw, string expected)
    {
        var result = Email.Create(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_empty_fails(string? raw)
    {
        var result = Email.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.EmailErrors.Empty);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing@dot")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@@example.com")]
    [InlineData("user name@example.com")]
    public void Create_invalid_format_fails(string raw)
    {
        var result = Email.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.EmailErrors.Invalid);
    }

    [Fact]
    public void Create_too_long_fails()
    {
        var raw = new string('a', 250) + "@b.co";

        var result = Email.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.EmailErrors.TooLong);
    }

    [Fact]
    public void Equal_emails_are_value_equal()
    {
        var a = Email.Create("user@example.com").Value;
        var b = Email.Create("USER@example.com").Value;

        a.Should().Be(b);
    }
}
