using BookSlot.Domain.Bookings;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Reports.BusiestSlots;

/// <summary>Heatmap-style report showing booking volume per (day-of-week, hour) bucket.</summary>
public static class GetBusiestSlotsReport
{
    /// <summary>One cell of the heatmap.</summary>
    public sealed record Cell(int DayOfWeek, int Hour, int Count);

    /// <summary>Response payload.</summary>
    public sealed record Response(
        DateTimeOffset From,
        DateTimeOffset To,
        string TimeZone,
        IReadOnlyList<Cell> Cells);

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

        /// <summary>Builds the heatmap. Buckets use the supplied IANA time zone.</summary>
        public async Task<Result<Response>> HandleAsync(
            DateTimeOffset? from, DateTimeOffset? to, string? timeZone, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();
            var fromUtc = (from ?? now.AddDays(-30)).ToUniversalTime();
            var toUtc = (to ?? now).ToUniversalTime();
            if (toUtc <= fromUtc)
                return Result.Failure<Response>(
                    Error.Validation("Reports.InvalidRange", "'to' must be greater than 'from'."));

            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone ?? "UTC"); }
            catch (TimeZoneNotFoundException)
            {
                return Result.Failure<Response>(
                    Error.Validation("Reports.InvalidTimeZone", "Unknown time zone identifier."));
            }

            // Only count bookings that were kept or actively canceled late; exclude Rescheduled (superseded).
            var starts = await _db.Bookings.AsNoTracking()
                .Where(b => b.StartUtc >= fromUtc && b.StartUtc < toUtc
                    && b.Status != BookingStatus.Rescheduled)
                .Select(b => b.StartUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var buckets = new int[7, 24];
            foreach (var startUtc in starts)
            {
                var local = TimeZoneInfo.ConvertTime(startUtc, tz);
                buckets[(int)local.DayOfWeek, local.Hour]++;
            }

            var cells = new List<Cell>();
            for (var d = 0; d < 7; d++)
            {
                for (var h = 0; h < 24; h++)
                {
                    if (buckets[d, h] > 0) cells.Add(new Cell(d, h, buckets[d, h]));
                }
            }

            return Result.Success(new Response(fromUtc, toUtc, tz.Id, cells));
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
            app.MapGet("/reports/busiest-slots", async (
                    DateTimeOffset? from, DateTimeOffset? to, string? timeZone,
                    Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(from, to, timeZone, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Reports.BusiestSlots")
                .WithTags("Reports")
                .RequireAuthorization("RequireStaff")
                .Produces<Response>()
                .ProducesValidationProblem();
        }
    }
}
