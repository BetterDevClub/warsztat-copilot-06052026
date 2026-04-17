using BookSlot.Domain.Bookings;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Reports.StaffUtilization;

/// <summary>Per-staff booked time over the reporting window.</summary>
/// <remarks>
/// Utilization is computed as total Confirmed + NoShow booked minutes per staff member.
/// A proper "% of available time" variant is deferred to Phase 24 when the availability
/// engine gains a bulk projection API.
/// </remarks>
public static class GetStaffUtilizationReport
{
    /// <summary>One row per staff member with a booked minute.</summary>
    public sealed record Row(
        Guid StaffId,
        string StaffName,
        int BookingCount,
        int BookedMinutes);

    /// <summary>Response payload.</summary>
    public sealed record Response(
        DateTimeOffset From,
        DateTimeOffset To,
        int WindowMinutes,
        IReadOnlyList<Row> Rows);

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

        /// <summary>Computes utilization rows for the window.</summary>
        public async Task<Result<Response>> HandleAsync(
            DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();
            var fromUtc = (from ?? now.AddDays(-30)).ToUniversalTime();
            var toUtc = (to ?? now).ToUniversalTime();
            if (toUtc <= fromUtc)
                return Result.Failure<Response>(
                    Error.Validation("Reports.InvalidRange", "'to' must be greater than 'from'."));

            var grouped = await _db.Bookings.AsNoTracking()
                .Where(b => b.StartUtc >= fromUtc && b.StartUtc < toUtc
                    && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.NoShow))
                .Select(b => new { b.StaffId, b.StartUtc, b.EndUtc })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var aggregated = grouped
                .GroupBy(x => x.StaffId)
                .Select(g => new
                {
                    StaffId = g.Key,
                    Count = g.Count(),
                    TotalMinutes = (int)g.Sum(x => (x.EndUtc - x.StartUtc).TotalMinutes),
                })
                .ToList();

            var staffIds = aggregated.Select(r => r.StaffId).ToList();
            var names = await _db.Staff.AsNoTracking()
                .Where(s => staffIds.Contains(s.Id))
                .Select(s => new { s.Id, s.DisplayName })
                .ToDictionaryAsync(s => s.Id, s => s.DisplayName, cancellationToken)
                .ConfigureAwait(false);

            var rows = aggregated
                .Select(r => new Row(
                    r.StaffId,
                    names.TryGetValue(r.StaffId, out var n) ? n : "(unknown)",
                    r.Count,
                    r.TotalMinutes))
                .OrderByDescending(r => r.BookedMinutes)
                .ToList();

            var windowMinutes = (int)Math.Max(0, (toUtc - fromUtc).TotalMinutes);
            return Result.Success(new Response(fromUtc, toUtc, windowMinutes, rows));
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
            app.MapGet("/reports/staff-utilization", async (
                    DateTimeOffset? from, DateTimeOffset? to, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(from, to, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Reports.StaffUtilization")
                .WithTags("Reports")
                .RequireAuthorization("RequireStaff")
                .Produces<Response>()
                .ProducesValidationProblem();
        }
    }
}
