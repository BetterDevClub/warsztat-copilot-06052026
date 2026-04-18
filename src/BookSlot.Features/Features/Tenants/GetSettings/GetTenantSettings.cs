using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Tenants.GetSettings;

/// <summary>Reads the current tenant's settings. Requires authentication (Viewer+).</summary>
public static class GetTenantSettings
{
    /// <summary>Response payload.</summary>
    public sealed record Response(
        Guid TenantId,
        string TimeZoneId,
        int BookingWindowDays,
        string? ContactEmail,
        string? BrandingPrimaryColor,
        string? BrandingLogoUrl,
        bool NoShowAutoMarkEnabled,
        int NoShowGracePeriodMinutes,
        DateTimeOffset? UpdatedAt);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        /// <summary>Fetches the settings for the resolved tenant.</summary>
        public async Task<Result<Response>> HandleAsync(CancellationToken cancellationToken)
        {
            var settings = await _db.TenantSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (settings is null)
            {
                return Result.Failure<Response>(TenantErrors.SettingsNotFound);
            }

            return Result.Success(new Response(
                _tenant.TenantId!.Value,
                settings.TimeZoneId,
                settings.BookingWindowDays,
                settings.ContactEmail,
                settings.BrandingPrimaryColor,
                settings.BrandingLogoUrl,
                settings.NoShowAutoMarkEnabled,
                settings.NoShowGracePeriodMinutes,
                settings.UpdatedAt));
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

            app.MapGet("/tenants/settings", async (Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Tenants.GetSettings")
                .WithTags("Tenants")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
