using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace BookSlot.ArchitectureTests;

public class NamingConventionTests
{
    private static readonly Assembly FeaturesAssembly =
        typeof(BookSlot.Features.Shared.FeatureHandlerServiceCollectionExtensions).Assembly;

    [Fact]
    public void Endpoint_classes_should_be_static()
    {
        var result = Types.InAssembly(FeaturesAssembly)
            .That()
            .HaveNameEndingWith("Endpoints")
            .Should()
            .BeStatic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Endpoint registration classes are minimal-API helpers and must be static. Offenders: " +
            (result.FailingTypeNames is null ? "<none>" : string.Join(", ", result.FailingTypeNames)));
    }

    [Fact]
    public void Handler_classes_should_be_sealed()
    {
        var result = Types.InAssembly(FeaturesAssembly)
            .That()
            .HaveNameEndingWith("Handler")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Slice handlers should be sealed (no inheritance across slices). Offenders: " +
            (result.FailingTypeNames is null ? "<none>" : string.Join(", ", result.FailingTypeNames)));
    }
}
