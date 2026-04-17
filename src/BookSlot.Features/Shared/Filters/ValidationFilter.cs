using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace BookSlot.Features.Shared.Filters;

/// <summary>
/// Endpoint filter that runs an <see cref="IValidator{T}"/> against the first argument
/// of the endpoint delegate whose type matches <typeparamref name="T"/>. On failure,
/// returns <see cref="Results.ValidationProblem(IDictionary{string, string[]}, string, string, int?, string, string, IDictionary{string, object?})"/> (HTTP 400) with per-field errors.
/// </summary>
/// <typeparam name="T">The request type to validate.</typeparam>
public sealed class ValidationFilter<T> : IEndpointFilter
    where T : class
{
    private readonly IValidator<T> _validator;

    /// <summary>Creates a new filter; <paramref name="validator"/> is resolved from DI.</summary>
    public ValidationFilter(IValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var target = context.Arguments.OfType<T>().FirstOrDefault();
        if (target is null)
        {
            return Results.Problem(
                detail: $"No argument of type {typeof(T).Name} found on the endpoint.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var result = await _validator.ValidateAsync(target, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        return Results.ValidationProblem(errors);
    }
}
