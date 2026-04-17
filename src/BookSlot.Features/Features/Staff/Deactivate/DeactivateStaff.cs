using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Staff.Deactivate;

/// <summary>Soft-deletes a staff member (sets <c>IsActive = false</c>). Owner only.</summary>
public static class DeactivateStaff
{
    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, TimeProvider clock) { _db = db; _clock = clock; }

        /// <summary>Loads, deactivates, saves.</summary>
        public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
        {
            var staff = await _db.Staff.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
            if (staff is null) return Result.Failure(StaffErrors.NotFound);
            staff.Deactivate(_clock.GetUtcNow());
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
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
            app.MapDelete("/staff/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Staff.Deactivate")
                .WithTags("Staff")
                .RequireAuthorization("RequireOwner");
        }
    }
}
