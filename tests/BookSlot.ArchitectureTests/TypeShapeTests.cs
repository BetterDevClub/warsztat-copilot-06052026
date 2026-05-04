using System.Reflection;
using FluentAssertions;

namespace BookSlot.ArchitectureTests;

public class TypeShapeTests
{
    private static readonly Assembly FeaturesAssembly =
        typeof(BookSlot.Features.Shared.FeatureHandlerServiceCollectionExtensions).Assembly;

    [Fact]
    public void Outer_slice_classes_should_be_static()
    {
        var sliceTypes = FeaturesAssembly.GetTypes()
            .Where(t => t.DeclaringType is null
                && t.Namespace?.StartsWith("BookSlot.Features.Features.") == true
                && t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(nt => nt.Name == "Handler"))
            .ToList();

        var nonStaticSlices = sliceTypes
            .Where(t => !(t.IsAbstract && t.IsSealed))
            .Select(t => t.FullName)
            .ToList();

        nonStaticSlices.Should().BeEmpty(
            $"Outer slice classes must be static (public static class). " +
            $"Offenders: {string.Join(", ", nonStaticSlices)}");
    }

    [Fact]
    public void Nested_Command_Query_Response_types_should_be_records()
    {
        var dtoTypes = FeaturesAssembly.GetTypes()
            .Where(t => t.DeclaringType is not null
                && t.DeclaringType.Namespace?.StartsWith("BookSlot.Features.Features.") == true
                && (t.Name == "Command" || t.Name == "Query" || t.Name == "Response"))
            .ToList();

        var nonRecordDtos = dtoTypes
            .Where(t => !IsRecord(t))
            .Select(t => $"{t.DeclaringType?.Name}.{t.Name}")
            .ToList();

        nonRecordDtos.Should().BeEmpty(
            $"Command/Query/Response types must be records (sealed record). " +
            $"Offenders: {string.Join(", ", nonRecordDtos)}");
    }

    [Fact]
    public void Nested_DTO_types_should_be_sealed()
    {
        var dtoTypes = FeaturesAssembly.GetTypes()
            .Where(t => t.DeclaringType is not null
                && t.DeclaringType.Namespace?.StartsWith("BookSlot.Features.Features.") == true
                && (t.Name == "Command" || t.Name == "Query" || t.Name == "Response" 
                    || t.Name == "Validator" || t.Name == "Handler"))
            .ToList();

        var unsealedDtos = dtoTypes
            .Where(t => !t.IsSealed)
            .Select(t => $"{t.DeclaringType?.Name}.{t.Name}")
            .ToList();

        unsealedDtos.Should().BeEmpty(
            $"Nested Command/Query/Response/Validator/Handler types must be sealed (no cross-slice inheritance). " +
            $"Offenders: {string.Join(", ", unsealedDtos)}");
    }

    [Fact]
    public void Validator_types_should_be_classes_not_records()
    {
        var validatorTypes = FeaturesAssembly.GetTypes()
            .Where(t => t.DeclaringType is not null
                && t.DeclaringType.Namespace?.StartsWith("BookSlot.Features.Features.") == true
                && t.Name == "Validator")
            .ToList();

        var recordValidators = validatorTypes
            .Where(IsRecord)
            .Select(t => $"{t.DeclaringType?.Name}.{t.Name}")
            .ToList();

        recordValidators.Should().BeEmpty(
            $"Validator types are logic (not data) and must be classes, not records. " +
            $"Offenders: {string.Join(", ", recordValidators)}");
    }

    [Fact]
    public void Outer_slice_classes_should_have_no_instance_state()
    {
        var sliceTypes = FeaturesAssembly.GetTypes()
            .Where(t => t.DeclaringType is null
                && t.Namespace?.StartsWith("BookSlot.Features.Features.") == true
                && t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(nt => nt.Name == "Handler"))
            .ToList();

        var slicesWithInstanceFields = sliceTypes
            .Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any())
            .Select(t => t.FullName)
            .ToList();

        slicesWithInstanceFields.Should().BeEmpty(
            $"Outer slice classes must be static and have no instance fields. " +
            $"Offenders: {string.Join(", ", slicesWithInstanceFields)}");
    }

    private static bool IsRecord(Type t) =>
        t.GetMethod("<Clone>$", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) != null
        || t.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) != null;
}
