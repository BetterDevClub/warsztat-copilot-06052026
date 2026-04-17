using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.AdminGetById;

/// <summary>Returns full admin-only booking details including internal notes and tokens.</summary>
public static class AdminGetBookingById
{
    /// <summary>Full admin view of a booking.</summary>
    public sealed record Response(
        Guid Id,
        Guid TenantId,
        Guid StaffId,
        string StaffName,
        Guid ServiceTypeId,
        string ServiceTypeName,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string Status,
        string GuestName,
        string GuestEmail,
        string? GuestPhone,
        string? GuestNotes,
        string? InternalNotes,
        Guid CancelToken,
        Guid RescheduleToken,
        Guid? RescheduledFromId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Loads the booking by id.</summary>
        public async Task<Result<Response>> HandleAsync(Guid id, CancellationToken cancellationToken)
        {
            var result = await (from b in _db.Bookings.AsNoTracking()
                                where b.Id == id
                                join s in _db.Staff.AsNoTracking() on b.StaffId equals s.Id
                                join t in _db.ServiceTypes.AsNoTracking() on b.ServiceTypeId equals t.Id
                                select new Response(
                                    b.Id, b.TenantId, b.StaffId, s.DisplayName,
                                    b.ServiceTypeId, t.Name, b.StartUtc, b.EndUtc,
                                    b.Status.ToString(),
                                    b.GuestName, b.GuestEmail, b.GuestPhone, b.GuestNotes,
                                    b.InternalNotes, b.CancelToken, b.RescheduleToken,
                                    b.RescheduledFromId, b.CreatedAt, b.UpdatedAt))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            return result is null
                ? Result.Failure<Response>(BookingFeatureErrors.BookingNotFound)
                : Result.Success(result);
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
            app.MapGet("/admin/bookings/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Bookings.AdminGetById")
                .WithTags("Bookings (Admin)")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
