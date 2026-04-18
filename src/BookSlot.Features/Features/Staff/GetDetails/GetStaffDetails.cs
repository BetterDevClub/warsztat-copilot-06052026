using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Staff.GetDetails;

/// <summary>Read model bundling staff profile with rules, overrides, and assigned service ids.</summary>
public static class GetStaffDetails
{
    /// <summary>Weekly rule DTO.</summary>
    public sealed record RuleDto(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

    /// <summary>Override DTO.</summary>
    public sealed record OverrideDto(Guid Id, DateOnly Date, bool IsUnavailable, TimeOnly? StartTime, TimeOnly? EndTime, string? Reason);

    /// <summary>Full response.</summary>
    public sealed record Response(
        Guid Id,
        string DisplayName,
        string? Title,
        string? Email,
        string? AvatarUrl,
        bool IsActive,
        IReadOnlyList<RuleDto> Rules,
        IReadOnlyList<OverrideDto> Overrides,
        IReadOnlyList<Guid> ServiceTypeIds);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Loads full details or returns NotFound.</summary>
        public async Task<Result<Response>> HandleAsync(Guid id, CancellationToken cancellationToken)
        {
            var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
            if (staff is null) return Result.Failure<Response>(StaffErrors.NotFound);

            var rules = await _db.AvailabilityRules.AsNoTracking()
                .Where(r => r.StaffId == id)
                .OrderBy(r => r.DayOfWeek).ThenBy(r => r.StartTime)
                .Select(r => new RuleDto(r.Id, r.DayOfWeek, r.StartTime, r.EndTime))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var overrides = await _db.AvailabilityOverrides.AsNoTracking()
                .Where(o => o.StaffId == id)
                .OrderBy(o => o.Date)
                .Select(o => new OverrideDto(o.Id, o.Date, o.IsUnavailable, o.StartTime, o.EndTime, o.Reason))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var services = await _db.StaffServiceAssignments.AsNoTracking()
                .Where(a => a.StaffId == id)
                .Select(a => a.ServiceTypeId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new Response(staff.Id, staff.DisplayName, staff.Title, staff.Email, staff.AvatarUrl, staff.IsActive,
                rules, overrides, services));
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
            app.MapGet("/staff/{id:guid}/details", async (Guid id, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Staff.GetDetails")
                .WithTags("Staff")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
