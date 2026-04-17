using System.Globalization;
using System.Text;
using BookSlot.Domain.Bookings;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.ExportCsv;

/// <summary>Streams the filtered booking list as a CSV download.</summary>
public static class ExportBookingsCsv
{
    private const int MaxRows = 10000;

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Builds the CSV body for the given filters.</summary>
        public async Task<string> HandleAsync(
            BookingStatus? status,
            Guid? staffId,
            Guid? serviceTypeId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken cancellationToken)
        {
            var query = _db.Bookings.AsNoTracking();
            if (status.HasValue) query = query.Where(b => b.Status == status.Value);
            if (staffId.HasValue) query = query.Where(b => b.StaffId == staffId.Value);
            if (serviceTypeId.HasValue) query = query.Where(b => b.ServiceTypeId == serviceTypeId.Value);
            if (from.HasValue) query = query.Where(b => b.StartUtc >= from.Value);
            if (to.HasValue) query = query.Where(b => b.StartUtc < to.Value);

            var rows = await (from b in query
                              join s in _db.Staff.AsNoTracking() on b.StaffId equals s.Id
                              join t in _db.ServiceTypes.AsNoTracking() on b.ServiceTypeId equals t.Id
                              orderby b.StartUtc
                              select new
                              {
                                  b.Id, b.StartUtc, b.EndUtc, b.Status,
                                  StaffName = s.DisplayName,
                                  ServiceTypeName = t.Name,
                                  b.GuestName, b.GuestEmail, b.GuestPhone,
                                  b.CreatedAt
                              })
                .Take(MaxRows)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var sb = new StringBuilder();
            sb.AppendLine("Id,StartUtc,EndUtc,Status,Staff,Service,GuestName,GuestEmail,GuestPhone,CreatedAt");

            var inv = CultureInfo.InvariantCulture;
            foreach (var r in rows)
            {
                sb.Append(r.Id).Append(',');
                sb.Append(r.StartUtc.UtcDateTime.ToString("O", inv)).Append(',');
                sb.Append(r.EndUtc.UtcDateTime.ToString("O", inv)).Append(',');
                sb.Append(r.Status).Append(',');
                sb.Append(Escape(r.StaffName)).Append(',');
                sb.Append(Escape(r.ServiceTypeName)).Append(',');
                sb.Append(Escape(r.GuestName)).Append(',');
                sb.Append(Escape(r.GuestEmail)).Append(',');
                sb.Append(Escape(r.GuestPhone)).Append(',');
                sb.Append(r.CreatedAt.UtcDateTime.ToString("O", inv));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var needsQuotes = value.IndexOfAny([',', '"', '\n', '\r']) >= 0;
            var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
            return needsQuotes ? $"\"{escaped}\"" : escaped;
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
            app.MapGet("/admin/bookings/export.csv", async (
                    BookingStatus? status,
                    Guid? staffId,
                    Guid? serviceTypeId,
                    DateTimeOffset? from,
                    DateTimeOffset? to,
                    Handler handler,
                    CancellationToken ct) =>
                {
                    var csv = await handler.HandleAsync(status, staffId, serviceTypeId, from, to, ct)
                        .ConfigureAwait(false);
                    var bytes = Encoding.UTF8.GetBytes(csv);
                    return Results.File(bytes, "text/csv; charset=utf-8", $"bookings-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
                })
                .WithName("Bookings.ExportCsv")
                .WithTags("Bookings (Admin)")
                .RequireAuthorization("RequireViewer")
                .Produces(StatusCodes.Status200OK, contentType: "text/csv");
        }
    }
}
