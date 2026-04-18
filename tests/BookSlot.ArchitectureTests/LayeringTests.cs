using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace BookSlot.ArchitectureTests;

public class LayeringTests
{
    private static readonly Assembly DomainAssembly =
        typeof(BookSlot.Domain.Primitives.Result).Assembly;

    private static readonly Assembly FeaturesAssembly =
        typeof(BookSlot.Features.Shared.FeatureHandlerServiceCollectionExtensions).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(BookSlot.Infrastructure.Persistence.AppDbContext).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_EFCore_or_AspNetCore_or_other_layers()
    {
        var forbidden = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "BookSlot.Infrastructure",
            "BookSlot.Features",
            "BookSlot.Api",
            "BookSlot.Web",
            "BookSlot.Worker",
        };

        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain must stay framework-agnostic. Offenders: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void Features_should_not_depend_on_host_projects()
    {
        var forbidden = new[]
        {
            "BookSlot.Api",
            "BookSlot.Web",
            "BookSlot.Worker",
        };

        var result = Types.InAssembly(FeaturesAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Slices must not depend on the host process. Offenders: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_host_projects_or_features()
    {
        var forbidden = new[]
        {
            "BookSlot.Api",
            "BookSlot.Web",
            "BookSlot.Worker",
            "BookSlot.Features",
        };

        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Infrastructure must not depend on hosts or feature slices. Offenders: {Format(result.FailingTypeNames)}");
    }

    private static string Format(System.Collections.Generic.IEnumerable<string>? names)
        => names is null ? "<none>" : string.Join(", ", names);
}
