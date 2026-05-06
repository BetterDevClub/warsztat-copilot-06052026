using System.Reflection;
using FluentAssertions;
using FluentValidation;
using NetArchTest.Rules;

namespace BookSlot.ArchitectureTests;

public class SliceContentRulesTests
{
    private static readonly Assembly FeaturesAssembly =
        typeof(BookSlot.Features.Shared.FeatureHandlerServiceCollectionExtensions).Assembly;

    [Fact]
    public void Slices_should_not_contain_Repository_Service_or_Manager_types()
    {
        var forbiddenSuffixes = new[] { "Repository", "Service", "Manager" };
        var allTypesInSlices = Types.InAssembly(FeaturesAssembly)
            .That()
            .ResideInNamespaceMatching(@"^BookSlot\.Features\.\w+\.\w+.*")
            .And()
            .DoNotResideInNamespaceMatching(@"^BookSlot\.Features\.Shared.*")
            .GetTypes();

        var offenders = allTypesInSlices
            .Where(t => t.IsClass 
                && !t.IsNested 
                && !(t.IsAbstract && t.IsSealed)  // Exclude static classes (static = abstract + sealed in IL)
                && forbiddenSuffixes.Any(suffix => t.Name.EndsWith(suffix)))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        offenders.Should().BeEmpty(
            $"VSA forbids Repository/Service/Manager instance classes inside slices. Use AppDbContext directly or put helpers in Shared. Slice outer static classes are allowed. Offenders: {Format(offenders)}");
    }

    [Fact]
    public void Validator_classes_in_slices_must_inherit_from_AbstractValidator()
    {
        var allTypesInSlices = Types.InAssembly(FeaturesAssembly)
            .That()
            .ResideInNamespaceMatching(@"^BookSlot\.Features\.\w+\.\w+.*")
            .And()
            .DoNotResideInNamespaceMatching(@"^BookSlot\.Features\.Shared.*")
            .GetTypes();

        var validatorClasses = allTypesInSlices
            .Where(t => t.IsClass && !t.IsAbstract && t.Name == "Validator")
            .ToList();

        var offenders = new List<string>();
        foreach (var validatorType in validatorClasses)
        {
            var inheritsFromAbstractValidator = validatorType.BaseType is not null
                && validatorType.BaseType.IsGenericType
                && validatorType.BaseType.GetGenericTypeDefinition() == typeof(AbstractValidator<>);

            if (!inheritsFromAbstractValidator)
                offenders.Add(validatorType.FullName ?? validatorType.Name);
        }

        offenders.Should().BeEmpty(
            $"Every Validator class in a slice must inherit from FluentValidation.AbstractValidator<TCommand>. Offenders: {Format(offenders)}");
    }

    [Fact]
    public void Handler_classes_must_be_in_slice_namespaces()
    {
        var result = Types.InAssembly(FeaturesAssembly)
            .That()
            .HaveNameEndingWith("Handler")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .Should()
            .ResideInNamespaceMatching(@"^BookSlot\.Features\.\w+\.\w+.*")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Handler classes must be in a slice namespace (BookSlot.Features.<Area>.<Operation>). No top-level handlers allowed. Offenders: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void Slice_core_types_must_be_nested_in_outer_class()
    {
        var coreTypeNames = new[] { "Command", "Query", "Response", "Validator", "Handler" };
        var allTypesInSlices = Types.InAssembly(FeaturesAssembly)
            .That()
            .ResideInNamespaceMatching(@"^BookSlot\.Features\.\w+\.\w+.*")
            .And()
            .DoNotResideInNamespaceMatching(@"^BookSlot\.Features\.Shared.*")
            .GetTypes();

        var offenders = allTypesInSlices
            .Where(t => coreTypeNames.Contains(t.Name) && t.DeclaringType is null)
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        offenders.Should().BeEmpty(
            $"Slice core types (Command/Query/Response/Validator/Handler) must be nested types inside an outer static class (co-location pattern). Orphaned types: {Format(offenders)}");
    }

    private static string Format(IEnumerable<string>? names)
        => names is null ? "<none>" : string.Join(", ", names);
}
