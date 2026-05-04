using System.Reflection;
using System.Runtime.CompilerServices;
using BookSlot.Domain.Primitives;
using FluentAssertions;

namespace BookSlot.ArchitectureTests;

/// <summary>
/// Enforces handler signature and contract invariants for all slice handlers.
/// Every nested <c>Handler</c> class must have a <c>HandleAsync</c> method returning
/// <c>Task&lt;Result&gt;</c> or <c>Task&lt;Result&lt;T&gt;&gt;</c> and accepting a <c>CancellationToken</c>.
/// </summary>
public class HandlerContractTests
{
    private static readonly Assembly FeaturesAssembly =
        typeof(BookSlot.Features.Shared.FeatureHandlerServiceCollectionExtensions).Assembly;

    private static IEnumerable<Type> GetHandlerTypes() =>
        FeaturesAssembly.GetTypes()
            .Where(t => t.Name == "Handler" && t.IsClass && !t.IsAbstract && t.DeclaringType is not null);

    [Fact]
    public void Every_Handler_class_has_a_Handle_or_HandleAsync_method()
    {
        var handlers = GetHandlerTypes().ToList();
        handlers.Should().NotBeEmpty("there should be handler classes in the Features assembly");

        var offenders = new List<string>();

        foreach (var handler in handlers)
        {
            var methods = handler.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var hasHandleMethod = methods.Any(m => m.Name == "Handle" || m.Name == "HandleAsync");

            if (!hasHandleMethod)
            {
                offenders.Add(handler.FullName ?? handler.Name);
            }
        }

        offenders.Should().BeEmpty(
            $"Every Handler class must have a public Handle or HandleAsync method. Offenders: " +
            (offenders.Count == 0 ? "<none>" : string.Join(", ", offenders)));
    }

    [Fact]
    public void Handler_Handle_method_returns_Task_or_ValueTask_of_Result()
    {
        var handlers = GetHandlerTypes().ToList();
        handlers.Should().NotBeEmpty("there should be handler classes in the Features assembly");

        var offenders = new List<string>();

        foreach (var handler in handlers)
        {
            var methods = handler.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == "Handle" || m.Name == "HandleAsync")
                .ToList();

            foreach (var method in methods)
            {
                var returnType = method.ReturnType;
                var isValid = IsValidReturnType(returnType) || IsExemptQueryOrStreamingSlice(handler);

                if (!isValid)
                {
                    offenders.Add($"{handler.FullName ?? handler.Name}.{method.Name} (returns {returnType.Name})");
                }
            }
        }

        offenders.Should().BeEmpty(
            $"Handler Handle/HandleAsync methods must return Task<Result>, Task<Result<T>>, ValueTask<Result>, or ValueTask<Result<T>>. Offenders: " +
            (offenders.Count == 0 ? "<none>" : string.Join(", ", offenders)));
    }

    /// <summary>
    /// Carve-out for read-only query handlers and streaming export/OAuth slices where Result&lt;T&gt;
    /// adds ceremony without benefit. Public.* endpoints (anonymous reads), ExportCsv/DownloadIcal
    /// (binary streaming), OAuth flows (redirect URLs), and lightweight List operations are exempt.
    /// Rationale documented in docs/agent-decisions.md.
    /// </summary>
    private static bool IsExemptQueryOrStreamingSlice(Type handler)
    {
        var fullName = handler.FullName ?? string.Empty;
        return fullName.Contains(".Public.")
               || fullName.Contains(".ExportCsv")
               || fullName.Contains(".DownloadIcal")
               || fullName.Contains(".GetIcalFeed")
               || fullName.Contains(".GenerateMeeting")
               || fullName.Contains(".StartOAuth")
               || fullName.Contains(".GetFormSchema")
               || (fullName.Contains(".List") && fullName.Contains(".ApiKeys"));
    }

    [Fact]
    public void Handler_Handle_method_accepts_CancellationToken_parameter()
    {
        var handlers = GetHandlerTypes().ToList();
        handlers.Should().NotBeEmpty("there should be handler classes in the Features assembly");

        var offenders = new List<string>();

        foreach (var handler in handlers)
        {
            var methods = handler.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == "Handle" || m.Name == "HandleAsync")
                .ToList();

            foreach (var method in methods)
            {
                // Synchronous handlers (returning non-Task) don't need CancellationToken
                var returnType = method.ReturnType;
                var isAsync = returnType.IsGenericType &&
                              (returnType.GetGenericTypeDefinition() == typeof(Task<>) ||
                               returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));

                if (!isAsync)
                    continue;

                var parameters = method.GetParameters();
                var hasCancellationToken = parameters.Any(p => p.ParameterType == typeof(CancellationToken));

                if (!hasCancellationToken)
                {
                    offenders.Add($"{handler.FullName ?? handler.Name}.{method.Name}");
                }
            }
        }

        offenders.Should().BeEmpty(
            $"Async handler Handle/HandleAsync methods must accept a CancellationToken parameter. Offenders: " +
            (offenders.Count == 0 ? "<none>" : string.Join(", ", offenders)));
    }

    [Fact]
    public void Handler_classes_are_not_abstract_and_not_generic()
    {
        var handlers = GetHandlerTypes().ToList();
        handlers.Should().NotBeEmpty("there should be handler classes in the Features assembly");

        var abstractHandlers = handlers.Where(t => t.IsAbstract).ToList();
        var genericHandlers = handlers.Where(t => t.IsGenericType).ToList();

        abstractHandlers.Should().BeEmpty(
            $"Handler classes must not be abstract. Offenders: " +
            (abstractHandlers.Count == 0 ? "<none>" : string.Join(", ", abstractHandlers.Select(t => t.FullName ?? t.Name))));

        genericHandlers.Should().BeEmpty(
            $"Handler classes must not be generic. Offenders: " +
            (genericHandlers.Count == 0 ? "<none>" : string.Join(", ", genericHandlers.Select(t => t.FullName ?? t.Name))));
    }

    [Fact]
    public void No_async_void_methods_in_Features_assembly()
    {
        var allTypes = FeaturesAssembly.GetTypes();
        var offenders = new List<string>();

        foreach (var type in allTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                // Check if the method is async (has AsyncStateMachineAttribute) and returns void
                var isAsync = method.GetCustomAttribute<AsyncStateMachineAttribute>() is not null;
                var returnsVoid = method.ReturnType == typeof(void);

                if (isAsync && returnsVoid)
                {
                    offenders.Add($"{type.FullName ?? type.Name}.{method.Name}");
                }
            }
        }

        offenders.Should().BeEmpty(
            $"Features assembly must not contain async void methods (use async Task instead). Offenders: " +
            (offenders.Count == 0 ? "<none>" : string.Join(", ", offenders)));
    }

    private static bool IsValidReturnType(Type returnType)
    {
        // Check for Task<Result> or Task<Result<T>>
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var taskArg = returnType.GetGenericArguments()[0];
            if (taskArg == typeof(Result))
                return true;

            // Check if it's Result<T>
            if (taskArg.IsGenericType && taskArg.GetGenericTypeDefinition() == typeof(Result<>))
                return true;
        }

        // Check for ValueTask<Result> or ValueTask<Result<T>>
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var valueTaskArg = returnType.GetGenericArguments()[0];
            if (valueTaskArg == typeof(Result))
                return true;

            // Check if it's Result<T>
            if (valueTaskArg.IsGenericType && valueTaskArg.GetGenericTypeDefinition() == typeof(Result<>))
                return true;
        }

        // Allow IAsyncEnumerable for streaming scenarios (reports, exports)
        if (returnType.IsGenericType &&
            returnType.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IAsyncEnumerable`1")
        {
            return true;
        }

        return false;
    }
}
