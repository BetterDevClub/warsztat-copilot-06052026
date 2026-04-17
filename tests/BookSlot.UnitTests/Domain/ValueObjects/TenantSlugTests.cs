using BookSlot.Domain.Primitives;
using BookSlot.Domain.ValueObjects;

namespace BookSlot.UnitTests.Domain.ValueObjects;

public class TenantSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-co")]
    [InlineData("some-big-company")]
    public void Create_valid_slug_succeeds(string raw)
    {
        var result = TenantSlug.Create(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(raw);
    }

    [Theory]
    [InlineData("www")]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("API")] // case-insensitive
    [InlineData("production")]
    public void Create_reserved_slug_fails(string raw)
    {
        var result = TenantSlug.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.SlugErrors.Reserved);
    }

    [Fact]
    public void Create_invalid_base_slug_fails_with_slug_error()
    {
        var result = TenantSlug.Create("ab");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.SlugErrors.TooShort);
    }
}
