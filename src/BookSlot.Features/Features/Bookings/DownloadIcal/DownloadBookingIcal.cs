using System.Globalization;
using System.Text;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.DownloadIcal;

/// <summary>
/// Produces a single-event RFC 5545 iCalendar file for a booking, suitable for
/// importing into Google Calendar / Outlook / Apple Calendar. Non-confirmed
/// bookings still render so staff can share cancellations if needed.
/// </summary>
public static class DownloadBookingIcal
{
    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Builds the iCalendar body for the given booking id.</summary>
        public async Task<string?> HandleAsync(Guid id, CancellationToken cancellationToken)
        {
            var row = await (from b in _db.Bookings.AsNoTracking()
                             where b.Id == id
                             join s in _db.Staff.AsNoTracking() on b.StaffId equals s.Id
                             join t in _db.ServiceTypes.AsNoTracking() on b.ServiceTypeId equals t.Id
                             select new
                             {
                                 b.Id, b.StartUtc, b.EndUtc, b.Status, b.GuestName, b.GuestEmail,
                                 b.GuestNotes, b.CreatedAt, b.UpdatedAt,
                                 StaffName = s.DisplayName, ServiceTypeName = t.Name
                             })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (row is null) return null;

            var inv = CultureInfo.InvariantCulture;
            static string Fmt(DateTimeOffset d) => d.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.Append("BEGIN:VCALENDAR\r\n");
            sb.Append("VERSION:2.0\r\n");
            sb.Append("PRODID:-//BookSlot//BookSlot//EN\r\n");
            sb.Append("CALSCALE:GREGORIAN\r\n");
            sb.Append("METHOD:PUBLISH\r\n");
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append("UID:").Append(row.Id.ToString("N", inv)).Append("@bookslot\r\n");
            sb.Append("DTSTAMP:").Append(Fmt((row.UpdatedAt ?? row.CreatedAt))).Append("\r\n");
            sb.Append("DTSTART:").Append(Fmt(row.StartUtc)).Append("\r\n");
            sb.Append("DTEND:").Append(Fmt(row.EndUtc)).Append("\r\n");
            sb.Append("SUMMARY:").Append(Escape($"{row.ServiceTypeName} — {row.GuestName}")).Append("\r\n");
            sb.Append("DESCRIPTION:").Append(Escape(BuildDescription(row.StaffName, row.GuestEmail, row.GuestNotes))).Append("\r\n");
            sb.Append("ORGANIZER;CN=").Append(Escape(row.StaffName)).Append(":mailto:noreply@bookslot.local\r\n");
            sb.Append("ATTENDEE;CN=").Append(Escape(row.GuestName)).Append(":mailto:").Append(row.GuestEmail).Append("\r\n");
            sb.Append("STATUS:").Append(row.Status switch
            {
                Domain.Bookings.BookingStatus.Cancelled => "CANCELLED",
                Domain.Bookings.BookingStatus.Rescheduled => "CANCELLED",
                _ => "CONFIRMED",
            }).Append("\r\n");
            sb.Append("END:VEVENT\r\n");
            sb.Append("END:VCALENDAR\r\n");
            return sb.ToString();
        }

        private static string BuildDescription(string staff, string guestEmail, string? notes)
        {
            var parts = new List<string> { $"Staff: {staff}", $"Guest: {guestEmail}" };
            if (!string.IsNullOrWhiteSpace(notes)) parts.Add($"Notes: {notes}");
            return string.Join(" \\n", parts);
        }

        // RFC 5545 TEXT escaping: backslash, comma, semicolon, newline.
        private static string Escape(string value) => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
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
            app.MapGet("/admin/bookings/{id:guid}/ical", async (Guid id, Handler handler, CancellationToken ct) =>
                {
                    var body = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    if (body is null) return Results.NotFound();
                    var bytes = Encoding.UTF8.GetBytes(body);
                    return Results.File(bytes, "text/calendar; charset=utf-8", $"booking-{id:N}.ics");
                })
                .WithName("Bookings.DownloadIcal")
                .WithTags("Bookings (Admin)")
                .RequireAuthorization("RequireViewer")
                .Produces(StatusCodes.Status200OK, contentType: "text/calendar")
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
