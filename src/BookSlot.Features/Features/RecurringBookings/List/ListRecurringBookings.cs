using BookSlot.Domain.Bookings;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.RecurringBookings.List;

/// <summary>Lists recurring booking templates for the tenant.</summary>
public static class ListRecurringBookings
{
    /// <summary>List item.</summary>
    public sealed record Item(
        Guid Id,
        Guid StaffId,
        string StaffName,
        Guid ServiceTypeId,
        string ServiceTypeName,
        int IntervalWeeks,
        DayOfWeek DayOfWeek,
        TimeOnly LocalStartTime,
        DateOnly StartDate,
        DateOnly? EndDate,
        string Status,
        string GuestName,
        string GuestEmail,
        DateOnly? LastGeneratedThrough,
        DateTimeOffset CreatedAt);

    /// <summary>Envelope.</summary>
    public sealed record Response(IReadOnlyList<Item> Items);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Returns all templates (optionally filtered by status).</summary>
        public async Task<Result<Response>> HandleAsync(
            RecurringBookingStatus? status,
            CancellationToken cancellationToken)
        {
            var query = _db.RecurringBookings.AsNoTracking();
            if (status.HasValue) query = query.Where(r => r.Status == status.Value);

            var items = await (from r in query
                               join s in _db.Staff.AsNoTracking() on r.StaffId equals s.Id
                               join t in _db.ServiceTypes.AsNoTracking() on r.ServiceTypeId equals t.Id
                               orderby r.CreatedAt descending
                               select new Item(
                                   r.Id, r.StaffId, s.DisplayName, r.ServiceTypeId, t.Name,
                                   r.IntervalWeeks, r.DayOfWeek, r.LocalStartTime,
                                   r.StartDate, r.EndDate, r.Status.ToString(),
                                   r.GuestName, r.GuestEmail, r.LastGeneratedThrough, r.CreatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(new Response(items));
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
            app.MapGet("/recurring-bookings", async (
                    RecurringBookingStatus? status, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(status, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("RecurringBookings.List")
                .WithTags("Recurring Bookings")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
