using BookSlot.Domain.Primitives;
using BookSlot.Domain.Services;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.ServiceTypes.SetFormSchema;

/// <summary>
/// Replaces (or clears) the custom booking form schema attached to a service type.
/// Schema JSON is parsed and validated server-side before persistence — a malformed
/// document is rejected with 400.
/// </summary>
public static class SetServiceTypeFormSchema
{
    /// <summary>Request body — pass null <c>SchemaJson</c> to clear the schema.</summary>
    public sealed record Command(string? SchemaJson);

    /// <summary>Response body.</summary>
    public sealed record Response(Guid ServiceTypeId, bool HasSchema, int FieldCount);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, TimeProvider clock)
        {
            _db = db;
            _clock = clock;
        }

        /// <summary>Persists the new schema on the service type.</summary>
        public async Task<Result<Response>> HandleAsync(Guid serviceTypeId, Command command, CancellationToken cancellationToken)
        {
            var service = await _db.ServiceTypes.FirstOrDefaultAsync(
                s => s.Id == serviceTypeId, cancellationToken).ConfigureAwait(false);
            if (service is null)
                return Result.Failure<Response>(Error.NotFound("ServiceType.NotFound", "Service type not found."));

            var now = _clock.GetUtcNow();
            var result = service.SetFormSchema(command.SchemaJson, now);
            if (result.IsFailure) return Result.Failure<Response>(result.Error);

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var fieldCount = 0;
            if (service.FormSchemaJson is not null)
            {
                var parsed = BookingFormSchema.Parse(service.FormSchemaJson);
                if (parsed.IsSuccess) fieldCount = parsed.Value.Fields.Count;
            }

            return Result.Success(new Response(service.Id, service.FormSchemaJson is not null, fieldCount));
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
            app.MapPut("/service-types/{id:guid}/form-schema", async (
                    Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("ServiceTypes.SetFormSchema")
                .WithTags("Service Types")
                .RequireAuthorization("RequireOwner")
                .Produces<Response>()
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
