using BookSlot.Domain.Bookings;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.AdminList;

/// <summary>
/// Returns a paginated list of bookings for admins, with optional filters on
/// status, staff, service, and time window.
/// </summary>
public static class AdminListBookings
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    /// <summary>List item.</summary>
    public sealed record Item(
        Guid Id,
        Guid StaffId,
        string StaffName,
        Guid ServiceTypeId,
        string ServiceTypeName,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string Status,
        string GuestName,
        string GuestEmail,
        DateTimeOffset CreatedAt);

    /// <summary>Envelope.</summary>
    public sealed record Response(
        IReadOnlyList<Item> Items,
        int Page,
        int PageSize,
        int TotalCount);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Runs the filtered paged query.</summary>
        public async Task<Result<Response>> HandleAsync(
            BookingStatus? status,
            Guid? staffId,
            Guid? serviceTypeId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = DefaultPageSize;
            if (pageSize > MaxPageSize) pageSize = MaxPageSize;

            var query = _db.Bookings.AsNoTracking();
            if (status.HasValue) query = query.Where(b => b.Status == status.Value);
            if (staffId.HasValue) query = query.Where(b => b.StaffId == staffId.Value);
            if (serviceTypeId.HasValue) query = query.Where(b => b.ServiceTypeId == serviceTypeId.Value);
            if (from.HasValue) query = query.Where(b => b.StartUtc >= from.Value);
            if (to.HasValue) query = query.Where(b => b.StartUtc < to.Value);

            var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            var items = await (from b in query
                               join s in _db.Staff.AsNoTracking() on b.StaffId equals s.Id
                               join t in _db.ServiceTypes.AsNoTracking() on b.ServiceTypeId equals t.Id
                               orderby b.StartUtc descending
                               select new Item(
                                   b.Id, b.StaffId, s.DisplayName, b.ServiceTypeId, t.Name,
                                   b.StartUtc, b.EndUtc, b.Status.ToString(),
                                   b.GuestName, b.GuestEmail, b.CreatedAt))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(new Response(items, page, pageSize, total));
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
            app.MapGet("/admin/bookings", async (
                    BookingStatus? status,
                    Guid? staffId,
                    Guid? serviceTypeId,
                    DateTimeOffset? from,
                    DateTimeOffset? to,
                    int? page,
                    int? pageSize,
                    Handler handler,
                    CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(
                        status, staffId, serviceTypeId, from, to,
                        page ?? 1, pageSize ?? DefaultPageSize, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Bookings.AdminList")
                .WithTags("Bookings (Admin)")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
