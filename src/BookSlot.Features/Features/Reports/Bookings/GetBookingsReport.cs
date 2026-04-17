using BookSlot.Domain.Bookings;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Reports.Bookings;

/// <summary>Aggregate booking KPIs (totals, cancellation rate, no-show rate) for the current tenant.</summary>
public static class GetBookingsReport
{
    /// <summary>Response payload.</summary>
    public sealed record Response(
        DateTimeOffset From,
        DateTimeOffset To,
        int Total,
        int Pending,
        int Confirmed,
        int Cancelled,
        int NoShow,
        int Rescheduled,
        double CancellationRate,
        double NoShowRate);

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

        /// <summary>Computes the aggregate KPIs.</summary>
        public async Task<Result<Response>> HandleAsync(
            DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();
            var fromUtc = (from ?? now.AddDays(-30)).ToUniversalTime();
            var toUtc = (to ?? now).ToUniversalTime();
            if (toUtc <= fromUtc)
                return Result.Failure<Response>(
                    Error.Validation("Reports.InvalidRange", "'to' must be greater than 'from'."));

            var buckets = await _db.Bookings.AsNoTracking()
                .Where(b => b.StartUtc >= fromUtc && b.StartUtc < toUtc)
                .GroupBy(b => b.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            int Get(BookingStatus s) => buckets.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            var pending = Get(BookingStatus.Pending);
            var confirmed = Get(BookingStatus.Confirmed);
            var cancelled = Get(BookingStatus.Cancelled);
            var noShow = Get(BookingStatus.NoShow);
            var rescheduled = Get(BookingStatus.Rescheduled);
            var total = pending + confirmed + cancelled + noShow + rescheduled;

            var cancelRate = total == 0 ? 0d : (double)cancelled / total;
            // No-show denominator uses bookings that actually reached the appointment window.
            var materialized = confirmed + noShow;
            var noShowRate = materialized == 0 ? 0d : (double)noShow / materialized;

            return Result.Success(new Response(
                fromUtc, toUtc, total, pending, confirmed, cancelled, noShow, rescheduled,
                Math.Round(cancelRate, 4), Math.Round(noShowRate, 4)));
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
            app.MapGet("/reports/bookings", async (
                    DateTimeOffset? from, DateTimeOffset? to, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(from, to, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Reports.Bookings")
                .WithTags("Reports")
                .RequireAuthorization("RequireStaff")
                .Produces<Response>()
                .ProducesValidationProblem();
        }
    }
}
