using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Staff.GetById;

/// <summary>Reads a single staff member by id.</summary>
public static class GetStaffById
{
    /// <summary>Response DTO.</summary>
    public sealed record Response(
        Guid Id,
        string DisplayName,
        string? Title,
        string? Email,
        string? AvatarUrl,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Loads or returns NotFound.</summary>
        public async Task<Result<Response>> HandleAsync(Guid id, CancellationToken cancellationToken)
        {
            var s = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
            if (s is null) return Result.Failure<Response>(StaffErrors.NotFound);
            return Result.Success(new Response(s.Id, s.DisplayName, s.Title, s.Email, s.AvatarUrl, s.IsActive, s.CreatedAt, s.UpdatedAt));
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
            app.MapGet("/staff/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Staff.GetById")
                .WithTags("Staff")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
