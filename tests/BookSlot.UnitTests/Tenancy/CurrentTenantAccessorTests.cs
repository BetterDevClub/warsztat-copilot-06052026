using BookSlot.Features.Shared.Tenancy;

namespace BookSlot.UnitTests.Tenancy;

public class CurrentTenantAccessorTests
{
    [Fact]
    public void Defaults_To_Unresolved()
    {
        var sut = new CurrentTenantAccessor();
        sut.IsResolved.Should().BeFalse();
        sut.TenantId.Should().BeNull();
        sut.Slug.Should().BeNull();
    }

    [Fact]
    public void Set_Assigns_And_Normalizes_Slug()
    {
        var sut = new CurrentTenantAccessor();
        var id = Guid.NewGuid();

        sut.Set(id, "  Acme  ");

        sut.IsResolved.Should().BeTrue();
        sut.TenantId.Should().Be(id);
        sut.Slug.Should().Be("acme");
    }

    [Fact]
    public void Clear_Returns_To_Unresolved()
    {
        var sut = new CurrentTenantAccessor();
        sut.Set(Guid.NewGuid(), "acme");
        sut.Clear();
        sut.IsResolved.Should().BeFalse();
        sut.TenantId.Should().BeNull();
        sut.Slug.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_Rejects_Missing_Slug(string? slug)
    {
        var sut = new CurrentTenantAccessor();
        var act = () => sut.Set(Guid.NewGuid(), slug!);
        act.Should().Throw<ArgumentException>();
    }
}
