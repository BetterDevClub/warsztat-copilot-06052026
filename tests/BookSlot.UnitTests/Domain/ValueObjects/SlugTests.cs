using BookSlot.Domain.Primitives;
using BookSlot.Domain.ValueObjects;

namespace BookSlot.UnitTests.Domain.ValueObjects;

public class SlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-co")]
    [InlineData("acme-co-2025")]
    [InlineData("  Acme-CO  ", "acme-co")]
    public void Create_valid_slug_succeeds(string raw, string? expected = null)
    {
        var result = Slug.Create(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected ?? raw);
    }

    [Theory]
    [InlineData(null, "Slug.Empty")]
    [InlineData("", "Slug.Empty")]
    [InlineData("ab", "Slug.TooShort")]
    [InlineData("-acme", "Slug.Invalid")]
    [InlineData("9acme", "Slug.Invalid")]
    [InlineData("acme--co", "Slug.Invalid")]
    [InlineData("acme-", "Slug.Invalid")]
    [InlineData("ACME!", "Slug.Invalid")]
    [InlineData("acme co", "Slug.Invalid")]
    public void Create_invalid_slug_fails(string? raw, string expectedCode)
    {
        var result = Slug.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void Create_too_long_fails()
    {
        var raw = new string('a', Slug.MaxLength + 1);

        var result = Slug.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.SlugErrors.TooLong);
    }
}
