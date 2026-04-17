using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BookSlot.Domain.Bookings;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using StackExchange.Redis;

namespace BookSlot.Features.Calendar.GetIcalFeed;

/// <summary>
/// Public, unauthenticated iCal subscription feed exposing a staff member's
/// confirmed bookings so guests and external calendar clients (Google / Apple /
/// Outlook) can subscribe. The feed is cached in Redis for a short TTL and
/// validated with ETag + Last-Modified to avoid re-serialising on every poll.
/// </summary>
public static class GetIcalFeed
{
    /// <summary>How long the serialised feed is cached in Redis.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>How far into the future events are included.</summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(365);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly IConnectionMultiplexer _redis;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, IConnectionMultiplexer redis, TimeProvider clock)
        {
            _db = db;
            _redis = redis;
            _clock = clock;
        }

        /// <summary>
        /// Result payload — an already-serialised iCal body with HTTP validator metadata.
        /// Returns null when the tenant/staff combination does not exist.
        /// </summary>
        public sealed record CachedFeed(string Body, string ETag, DateTimeOffset LastModified);

        /// <summary>Builds (or reads from cache) the iCal feed for the given tenant slug + staff id.</summary>
        public async Task<CachedFeed?> HandleAsync(string tenantSlug, Guid staffId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tenantSlug);

            // Tenant-filtered DbSets would return nothing for this anonymous request; opt out
            // of the global filter and resolve the tenant by slug explicitly.
            var tenant = await _db.Tenants.AsNoTracking()
                .Where(t => t.Slug == tenantSlug && t.IsActive)
                .Select(t => new { t.Id, t.Name })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (tenant is null) return null;

            var cacheKey = $"bookslot:ical:{tenant.Id:N}:{staffId:N}";
            var redis = _redis.GetDatabase();
            var cached = await redis.StringGetAsync(cacheKey).ConfigureAwait(false);
            if (cached.HasValue)
            {
                var parsed = DeserializeCache(cached!);
                if (parsed is not null) return parsed;
            }

            var now = _clock.GetUtcNow();
            var horizonEnd = now + Horizon;

            var staff = await _db.Staff.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Id == staffId && s.TenantId == tenant.Id && s.IsActive)
                .Select(s => new { s.Id, s.DisplayName })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (staff is null) return null;

            var bookings = await _db.Bookings.IgnoreQueryFilters().AsNoTracking()
                .Where(b => b.TenantId == tenant.Id
                         && b.StaffId == staffId
                         && b.Status == BookingStatus.Confirmed
                         && b.StartUtc >= now
                         && b.StartUtc < horizonEnd)
                .Join(_db.ServiceTypes.IgnoreQueryFilters().AsNoTracking(),
                      b => b.ServiceTypeId, st => st.Id,
                      (b, st) => new
                      {
                          b.Id, b.StartUtc, b.EndUtc, b.GuestName, b.GuestEmail,
                          b.CreatedAt, b.UpdatedAt, ServiceTypeName = st.Name,
                      })
                .OrderBy(b => b.StartUtc)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var body = BuildBody(tenant.Name, staff.DisplayName, bookings.Select(b =>
                new BookingEvent(b.Id, b.StartUtc, b.EndUtc, b.ServiceTypeName,
                    b.GuestName, b.GuestEmail, b.UpdatedAt ?? b.CreatedAt)));

            var etag = BuildETag(body);
            var lastModified = bookings.Count == 0
                ? now
                : bookings.Max(b => b.UpdatedAt ?? b.CreatedAt);

            var feed = new CachedFeed(body, etag, lastModified);
            await redis.StringSetAsync(cacheKey, SerializeCache(feed), CacheTtl).ConfigureAwait(false);
            return feed;
        }

        // -------------------------------------------------------------------

        private sealed record BookingEvent(
            Guid Id, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string ServiceTypeName,
            string GuestName, string GuestEmail, DateTimeOffset LastModified);

        private static string BuildBody(string tenantName, string staffName, IEnumerable<BookingEvent> events)
        {
            var inv = CultureInfo.InvariantCulture;
            static string Fmt(DateTimeOffset d) => d.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.Append("BEGIN:VCALENDAR\r\n");
            sb.Append("VERSION:2.0\r\n");
            sb.Append("PRODID:-//BookSlot//BookSlot//EN\r\n");
            sb.Append("CALSCALE:GREGORIAN\r\n");
            sb.Append("METHOD:PUBLISH\r\n");
            sb.Append("X-WR-CALNAME:").Append(Escape($"{tenantName} — {staffName}")).Append("\r\n");
            foreach (var e in events)
            {
                sb.Append("BEGIN:VEVENT\r\n");
                sb.Append("UID:").Append(e.Id.ToString("N", inv)).Append("@bookslot\r\n");
                sb.Append("DTSTAMP:").Append(Fmt(e.LastModified)).Append("\r\n");
                sb.Append("DTSTART:").Append(Fmt(e.StartUtc)).Append("\r\n");
                sb.Append("DTEND:").Append(Fmt(e.EndUtc)).Append("\r\n");
                sb.Append("SUMMARY:").Append(Escape($"{e.ServiceTypeName} — {e.GuestName}")).Append("\r\n");
                sb.Append("ORGANIZER;CN=").Append(Escape(staffName)).Append(":mailto:noreply@bookslot.local\r\n");
                sb.Append("STATUS:CONFIRMED\r\n");
                sb.Append("END:VEVENT\r\n");
            }
            sb.Append("END:VCALENDAR\r\n");
            return sb.ToString();
        }

        private static string BuildETag(string body)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(body));
            return "\"" + Convert.ToHexString(hash)[..16].ToLowerInvariant() + "\"";
        }

        private static string Escape(string value) => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        private static string SerializeCache(CachedFeed feed)
            => feed.LastModified.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
               + "|" + feed.ETag + "|" + feed.Body;

        private static CachedFeed? DeserializeCache(string raw)
        {
            var first = raw.IndexOf('|', StringComparison.Ordinal);
            if (first < 0) return null;
            var second = raw.IndexOf('|', first + 1);
            if (second < 0) return null;
            if (!long.TryParse(raw.AsSpan(0, first), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
                return null;
            var etag = raw.Substring(first + 1, second - first - 1);
            var body = raw[(second + 1)..];
            return new CachedFeed(body, etag, DateTimeOffset.FromUnixTimeSeconds(unix));
        }
    }

    /// <summary>Endpoint registration.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.Public;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.MapGet("/calendar/{tenantSlug}/{staffId:guid}.ics", async (
                    string tenantSlug, Guid staffId, HttpContext http, Handler handler, CancellationToken ct) =>
                {
                    var feed = await handler.HandleAsync(tenantSlug, staffId, ct).ConfigureAwait(false);
                    if (feed is null) return Results.NotFound();

                    var headers = http.Request.Headers;
                    if (headers.TryGetValue(HeaderNames.IfNoneMatch, out var etagHeader)
                        && etagHeader.ToString().Contains(feed.ETag, StringComparison.Ordinal))
                    {
                        return Results.StatusCode(StatusCodes.Status304NotModified);
                    }

                    http.Response.Headers[HeaderNames.ETag] = feed.ETag;
                    http.Response.Headers[HeaderNames.LastModified] = feed.LastModified.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);
                    http.Response.Headers[HeaderNames.CacheControl] = "public, max-age=300";
                    var bytes = Encoding.UTF8.GetBytes(feed.Body);
                    return Results.File(bytes, "text/calendar; charset=utf-8", $"{tenantSlug}-{staffId:N}.ics");
                })
                .WithName("Calendar.GetIcalFeed")
                .WithTags("Calendar")
                .AllowAnonymous()
                .Produces(StatusCodes.Status200OK, contentType: "text/calendar")
                .Produces(StatusCodes.Status304NotModified)
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
