using BookSlot.Features.Shared.Tenancy;

namespace BookSlot.UnitTests.Tenancy;

public class TenantIdFactoryTests
{
    [Fact]
    public void FromSlug_IsDeterministic()
    {
        var a = TenantIdFactory.FromSlug("acme");
        var b = TenantIdFactory.FromSlug("acme");
        a.Should().Be(b);
    }

    [Fact]
    public void FromSlug_NormalizesCaseAndWhitespace()
    {
        var canonical = TenantIdFactory.FromSlug("acme");
        TenantIdFactory.FromSlug("ACME").Should().Be(canonical);
        TenantIdFactory.FromSlug("  Acme  ").Should().Be(canonical);
    }

    [Fact]
    public void FromSlug_DifferentSlugs_ProduceDifferentIds()
    {
        TenantIdFactory.FromSlug("acme").Should().NotBe(TenantIdFactory.FromSlug("globex"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromSlug_RejectsMissingSlug(string? slug)
    {
        var act = () => TenantIdFactory.FromSlug(slug!);
        act.Should().Throw<ArgumentException>();
    }
}
