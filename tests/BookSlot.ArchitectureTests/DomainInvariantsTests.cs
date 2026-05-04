using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.ArchitectureTests;

/// <summary>
/// Enforces Domain layer purity beyond dependency checks in LayeringTests.
/// Validates structural invariants: tenant-scoped entities, sealed value objects,
/// entity constructor patterns, and no mutable public fields.
/// </summary>
public class DomainInvariantsTests
{
    private static readonly Assembly DomainAssembly =
        typeof(ITenantScoped).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_SystemNetHttp()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("System.Net.Http")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain must not depend on System.Net.Http. Offenders: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void Domain_should_not_depend_on_MicrosoftExtensionsConfiguration()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Extensions.Configuration")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain must not depend on Microsoft.Extensions.Configuration. Offenders: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void All_ITenantScoped_implementers_must_expose_Guid_TenantId_property()
    {
        var implementers = DomainAssembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false })
            .Where(t => typeof(ITenantScoped).IsAssignableFrom(t))
            .ToList();

        var offenders = new List<string>();

        foreach (var type in implementers)
        {
            var property = type.GetProperty(
                "TenantId",
                BindingFlags.Public | BindingFlags.Instance);

            if (property is null || property.PropertyType != typeof(Guid))
            {
                offenders.Add(type.FullName ?? type.Name);
            }
        }

        offenders.Should().BeEmpty(
            $"All ITenantScoped implementers must expose a public Guid TenantId property. Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Concrete_entities_should_not_have_public_parameterless_constructors()
    {
        var entityType = typeof(Entity<>);
        var aggregateRootType = typeof(AggregateRoot<>);

        var concreteEntities = DomainAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => IsSubclassOfRawGeneric(entityType, t) || IsSubclassOfRawGeneric(aggregateRootType, t))
            .ToList();

        var offenders = new List<string>();

        foreach (var type in concreteEntities)
        {
            var publicParameterlessCtors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Where(c => c.GetParameters().Length == 0)
                .ToList();

            if (publicParameterlessCtors.Any())
            {
                offenders.Add(type.FullName ?? type.Name);
            }
        }

        offenders.Should().BeEmpty(
            $"Concrete entities should not have public parameterless constructors. " +
            $"Use private/protected parameterless ctors for EF Core, or static factory methods. " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ValueObjects_must_be_sealed()
    {
        var valueObjectType = typeof(ValueObject);

        var concreteValueObjects = DomainAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => valueObjectType.IsAssignableFrom(t))
            .Where(t => t != valueObjectType)
            .ToList();

        var offenders = concreteValueObjects
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        offenders.Should().BeEmpty(
            $"All concrete ValueObject subclasses must be sealed. Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Domain_should_not_have_public_mutable_fields()
    {
        var allTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass)
            .Where(t => !IsCompilerGenerated(t))  // Exclude compiler-generated state machines
            .ToList();

        var offenders = new List<string>();

        foreach (var type in allTypes)
        {
            var publicMutableFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .ToList();

            foreach (var field in publicMutableFields)
            {
                offenders.Add($"{type.FullName ?? type.Name}.{field.Name}");
            }
        }

        offenders.Should().BeEmpty(
            $"Domain types must not have public mutable fields. Use properties with private setters. " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.Contains('<') || type.Name.Contains('>') || 
        type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;

    private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
    {
        while (toCheck is not null && toCheck != typeof(object))
        {
            var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
            if (generic == cur)
            {
                return true;
            }

            toCheck = toCheck.BaseType!;
        }

        return false;
    }

    private static string Format(IEnumerable<string>? names)
        => names is null ? "<none>" : string.Join(", ", names);
}
