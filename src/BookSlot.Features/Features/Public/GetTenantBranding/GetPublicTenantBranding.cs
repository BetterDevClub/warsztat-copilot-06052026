using BookSlot.Domain.Abstractions;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Public.GetTenantBranding;

/// <summary>Anonymous lookup of tenant branding + display name + timezone for the public booking page header.</summary>
public static class GetPublicTenantBranding
{
    /// <summary>Response payload.</summary>
    public sealed record Response(
        string DisplayName,
        string Slug,
        string TimeZoneId,
        int BookingWindowDays,
        string? ContactEmail,
        string? BrandingPrimaryColor,
        string? BrandingLogoUrl);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _currentTenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant currentTenant)
        {
            _db = db;
            _currentTenant = currentTenant;
        }

        /// <summary>Returns branding for the resolved tenant, or null if not found.</summary>
        public async Task<Response?> HandleAsync(CancellationToken cancellationToken)
        {
            var tenantId = _currentTenant.TenantId;
            if (tenantId is null) return null;
            var tenant = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId.Value && t.IsActive, cancellationToken).ConfigureAwait(false);
            if (tenant is null) return null;
            var settings = await _db.TenantSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId.Value, cancellationToken).ConfigureAwait(false);
            return new Response(
                tenant.Name,
                tenant.Slug,
                settings?.TimeZoneId ?? "UTC",
                settings?.BookingWindowDays ?? 30,
                settings?.ContactEmail,
                settings?.BrandingPrimaryColor,
                settings?.BrandingLogoUrl);
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
            app.MapGet("/public/tenant", async (Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(ct).ConfigureAwait(false);
                    return result is null ? Results.NotFound() : Results.Ok(result);
                })
                .WithName("Public.GetTenantBranding")
                .WithTags("Public")
                .AllowAnonymous()
                .Produces<Response>();
        }
    }
}
