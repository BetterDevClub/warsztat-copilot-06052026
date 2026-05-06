using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace BookSlot.ArchitectureTests;

public class InfrastructureBoundaryTests
{
    private static readonly Assembly FeaturesAssembly =
        typeof(BookSlot.Features.Shared.FeatureHandlerServiceCollectionExtensions).Assembly;

    [Fact]
    public void Features_should_not_use_HttpClient_directly()
    {
        var offenders = FeaturesAssembly.GetTypes()
            .SelectMany(t => new[]
            {
                t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(f => f.FieldType == typeof(System.Net.Http.HttpClient))
                    .Select(f => $"{t.FullName}.{f.Name} (field)"),
                
                t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(p => p.PropertyType == typeof(System.Net.Http.HttpClient))
                    .Select(p => $"{t.FullName}.{p.Name} (property)"),
                
                t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .SelectMany(c => c.GetParameters()
                        .Where(p => p.ParameterType == typeof(System.Net.Http.HttpClient))
                        .Select(p => $"{t.FullName}..ctor({p.Name}) (constructor parameter)"))
            }.SelectMany(x => x))
            .ToList();

        offenders.Should().BeEmpty(
            $"Features must use IHttpClientFactory instead of HttpClient directly. " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Features_outside_Auth_should_not_depend_on_AspNetCore_Identity()
    {
        // Auth.* and Tenants.Register.* slices ARE the identity boundary and legitimately use
        // UserManager/SignInManager. All other slices must stay insulated from Identity.
        var result = Types.InAssembly(FeaturesAssembly)
            .That()
            .DoNotResideInNamespaceMatching("BookSlot.Features.Auth.*")
            .And()
            .DoNotResideInNamespaceMatching("BookSlot.Features.Tenants.Register.*")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore.Identity")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Identity wiring belongs in Infrastructure/Api, not Features (except Auth/Tenants.Register). " +
            $"Offenders: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void Features_should_not_depend_on_Npgsql()
    {
        var result = Types.InAssembly(FeaturesAssembly)
            .ShouldNot()
            .HaveDependencyOn("Npgsql")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Raw SQL is forbidden in Features; use EF Core only. " +
            $"Offenders: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void Worker_BackgroundService_types_should_be_sealed()
    {
        var workerAssembly = Assembly.Load("BookSlot.Worker");
        var backgroundServiceType = typeof(Microsoft.Extensions.Hosting.BackgroundService);

        var offenders = workerAssembly.GetTypes()
            .Where(t => !t.IsAbstract
                && backgroundServiceType.IsAssignableFrom(t)
                && !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty(
            $"BackgroundService implementations must be sealed (no inheritance in Worker). " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Api_should_not_depend_on_Worker()
    {
        var apiAssembly = Assembly.Load("BookSlot.Api");

        var result = Types.InAssembly(apiAssembly)
            .ShouldNot()
            .HaveDependencyOn("BookSlot.Worker")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"API host must not depend on Worker assembly. " +
            $"Offenders: {Format(result.FailingTypeNames)}");
    }

    private static string Format(System.Collections.Generic.IEnumerable<string>? names)
        => names is null ? "<none>" : string.Join(", ", names);
}
