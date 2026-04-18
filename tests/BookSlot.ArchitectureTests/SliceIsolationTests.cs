using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace BookSlot.ArchitectureTests;

/// <summary>
/// Vertical Slice Architecture rule: types in one slice area must not depend on types
/// in a different slice area. Cross-cutting helpers live in <c>BookSlot.Features.Shared</c>.
/// </summary>
public class SliceIsolationTests
{
    private const string FeaturesRoot = "BookSlot.Features";
    private const string SharedNamespace = "BookSlot.Features.Shared";

    private static readonly Assembly FeaturesAssembly =
        typeof(BookSlot.Features.Shared.FeatureHandlerServiceCollectionExtensions).Assembly;

    public static IEnumerable<object[]> SliceAreas()
    {
        var areas = FeaturesAssembly.GetTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns is not null && ns.StartsWith(FeaturesRoot + ".", StringComparison.Ordinal))
            .Where(ns => !ns!.StartsWith(SharedNamespace, StringComparison.Ordinal))
            .Select(ns => ns!.Substring(FeaturesRoot.Length + 1).Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        foreach (var area in areas)
        {
            yield return new object[] { area };
        }
    }

    [Theory]
    [MemberData(nameof(SliceAreas))]
    public void Slice_should_not_depend_on_other_slices(string area)
    {
        var areaNamespace = $"{FeaturesRoot}.{area}";
        var otherAreas = SliceAreas()
            .Select(x => (string)x[0])
            .Where(x => !string.Equals(x, area, StringComparison.Ordinal))
            .Select(x => $"{FeaturesRoot}.{x}.")
            .ToArray();

        if (otherAreas.Length == 0)
        {
            return;
        }

        var result = Types.InAssembly(FeaturesAssembly)
            .That()
            .ResideInNamespaceStartingWith(areaNamespace + ".")
            .Or()
            .ResideInNamespace(areaNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(otherAreas)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Slice '{area}' must not reach into other slices. Offenders: " +
            (result.FailingTypeNames is null ? "<none>" : string.Join(", ", result.FailingTypeNames)));
    }
}
