using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.ServiceTypes.GetFormSchema;

/// <summary>
/// Returns the raw form schema JSON for a service type so the public booking
/// page can render the dynamic fields. Anonymous — needed by un-authenticated
/// guests on the public flow.
/// </summary>
public static class GetServiceTypeFormSchema
{
    /// <summary>Response body — <c>SchemaJson</c> is null when no schema is configured.</summary>
    public sealed record Response(Guid ServiceTypeId, string? SchemaJson);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Loads the schema (or null) for the given service type.</summary>
        public async Task<Response?> HandleAsync(Guid serviceTypeId, CancellationToken cancellationToken)
        {
            var row = await _db.ServiceTypes.AsNoTracking()
                .Where(s => s.Id == serviceTypeId && s.IsActive)
                .Select(s => new { s.Id, s.FormSchemaJson })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            return row is null ? null : new Response(row.Id, row.FormSchemaJson);
        }
    }

    /// <summary>Endpoint registration.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.TenantScoped;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.MapGet("/service-types/{id:guid}/form-schema", async (
                    Guid id, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    return result is null ? Results.NotFound() : Results.Ok(result);
                })
                .WithName("ServiceTypes.GetFormSchema")
                .WithTags("Service Types")
                .AllowAnonymous()
                .Produces<Response>()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
