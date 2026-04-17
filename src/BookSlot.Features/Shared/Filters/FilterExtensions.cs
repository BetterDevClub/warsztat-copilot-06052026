using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BookSlot.Features.Shared.Filters;

/// <summary>
/// Fluent helpers that make per-slice filter wire-up read well at the endpoint definition site:
/// <code>app.MapPost("...", handler).WithValidation&lt;MyCommand&gt;();</code>
/// </summary>
public static class FilterExtensions
{
    /// <summary>Adds a <see cref="ValidationFilter{T}"/> to this endpoint.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder)
        where T : class
        => builder.AddEndpointFilter<ValidationFilter<T>>();
}
