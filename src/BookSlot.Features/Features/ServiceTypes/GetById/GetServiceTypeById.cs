using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.ServiceTypes.GetById;

/// <summary>Reads a single service type by id within the current tenant.</summary>
public static class GetServiceTypeById
{
    /// <summary>Response DTO.</summary>
    public sealed record Response(
        Guid Id,
        string Name,
        string Slug,
        int DurationMinutes,
        int BufferBeforeMinutes,
        int BufferAfterMinutes,
        decimal Price,
        string Currency,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Loads the service type or returns NotFound.</summary>
        public async Task<Result<Response>> HandleAsync(Guid id, CancellationToken cancellationToken)
        {
            var s = await _db.ServiceTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (s is null)
            {
                return Result.Failure<Response>(ServiceTypeErrors.NotFound);
            }

            return Result.Success(new Response(
                s.Id,
                s.Name,
                s.Slug,
                s.DurationMinutes,
                s.BufferBeforeMinutes,
                s.BufferAfterMinutes,
                s.Price,
                s.Currency,
                s.Description,
                s.IsActive,
                s.CreatedAt,
                s.UpdatedAt));
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

            app.MapGet("/service-types/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("ServiceTypes.GetById")
                .WithTags("ServiceTypes")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
