using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Tenants.UpdateSettings;

/// <summary>Replaces the current tenant's settings. Restricted to <c>Owner</c>.</summary>
public static class UpdateTenantSettings
{
    /// <summary>Request body — full replacement, not a patch.</summary>
    public sealed record Command(
        string TimeZoneId,
        int BookingWindowDays,
        string? ContactEmail,
        string? BrandingPrimaryColor,
        string? BrandingLogoUrl);

    /// <summary>FluentValidation rules. Domain enforces the deep checks (TZ id recognised, window in range).</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(80);
            RuleFor(x => x.BookingWindowDays).InclusiveBetween(1, 365);
            RuleFor(x => x.ContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
            RuleFor(x => x.BrandingPrimaryColor).MaximumLength(16);
            RuleFor(x => x.BrandingLogoUrl).MaximumLength(1024);
        }
    }

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

        /// <summary>Applies the update in-place.</summary>
        public async Task<Result> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            var settings = await _db.TenantSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (settings is null)
            {
                return Result.Failure(TenantErrors.SettingsNotFound);
            }

            var update = settings.Update(
                command.TimeZoneId,
                command.BookingWindowDays,
                command.ContactEmail,
                command.BrandingPrimaryColor,
                command.BrandingLogoUrl,
                _clock.GetUtcNow());

            if (update.IsFailure)
            {
                return update;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
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

            app.MapPut("/tenants/settings", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Tenants.UpdateSettings")
                .WithTags("Tenants")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner");
        }
    }
}
