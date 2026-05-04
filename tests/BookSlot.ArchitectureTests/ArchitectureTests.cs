using FluentAssertions;

namespace BookSlot.ArchitectureTests;

/// <summary>
/// Architecture test aggregator. All actual architecture invariants are enforced by dedicated test classes:
/// <list type="bullet">
/// <item><description><see cref="LayeringTests"/> — ensures clean separation between Domain, Infrastructure, and Features layers.
/// Domain must remain pure (no EF Core, no ASP.NET Core dependencies).</description></item>
/// <item><description><see cref="SliceIsolationTests"/> — enforces Vertical Slice Architecture: slices in different feature areas
/// must not reference each other; cross-cutting helpers belong in <c>BookSlot.Features.Shared</c>.</description></item>
/// <item><description><see cref="NamingConventionTests"/> — verifies naming conventions (e.g., *Endpoints classes are static,
/// *Handler classes are sealed) to maintain consistency across the codebase.</description></item>
/// <item><description><see cref="SliceContentRulesTests"/> — enforces VSA slice content rules: only allowed types per slice folder
/// (*Endpoints, *Handler, *Validator, Command/Query/Response records).</description></item>
/// <item><description><see cref="HandlerContractTests"/> — enforces handler signature contracts: Result&lt;T&gt; returns (with exemptions for
/// streaming/query slices), CancellationToken for async methods, no async void.</description></item>
/// <item><description><see cref="TypeShapeTests"/> — enforces type-level structural rules: handlers sealed, validators sealed,
/// endpoint classes static, Command/Query/Response as records.</description></item>
/// <item><description><see cref="DomainInvariantsTests"/> — enforces Domain layer purity beyond dependency checks: tenant-scoped entities,
/// sealed value objects, no public parameterless constructors, no mutable fields.</description></item>
/// <item><description><see cref="InfrastructureBoundaryTests"/> — enforces infrastructure boundaries: no raw HttpClient in Features,
/// no Npgsql (EF Core only), BackgroundService implementations sealed, API not depending on Worker.</description></item>
/// </list>
/// This aggregator class provides a single discovery test to ensure none of the dedicated test classes are accidentally removed.
/// </summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void Architecture_guardrails_are_enforced_by_dedicated_test_classes()
    {
        var asm = typeof(ArchitectureTests).Assembly;
        var types = asm.GetTypes().Select(t => t.Name).ToHashSet();

        types.Should().Contain(nameof(LayeringTests),
            "LayeringTests enforce clean separation between Domain, Infrastructure, and Features layers");

        types.Should().Contain(nameof(SliceIsolationTests),
            "SliceIsolationTests enforce Vertical Slice Architecture: no cross-slice dependencies");

        types.Should().Contain(nameof(NamingConventionTests),
            "NamingConventionTests verify naming conventions (*Endpoints static, *Handler sealed)");

        types.Should().Contain(nameof(SliceContentRulesTests),
            "SliceContentRulesTests enforce allowed types per VSA slice folder");

        types.Should().Contain(nameof(HandlerContractTests),
            "HandlerContractTests enforce handler signature and return type contracts");

        types.Should().Contain(nameof(TypeShapeTests),
            "TypeShapeTests enforce type-level structural rules (sealed/static/record)");

        types.Should().Contain(nameof(DomainInvariantsTests),
            "DomainInvariantsTests enforce Domain layer structural purity");

        types.Should().Contain(nameof(InfrastructureBoundaryTests),
            "InfrastructureBoundaryTests enforce infrastructure boundaries");
    }
}
