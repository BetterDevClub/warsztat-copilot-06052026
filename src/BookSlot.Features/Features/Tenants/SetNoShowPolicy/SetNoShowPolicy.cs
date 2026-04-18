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

namespace BookSlot.Features.Tenants.SetNoShowPolicy;

/// <summary>Updates the no-show auto-marker policy for the current tenant. Restricted to <c>Owner</c>.</summary>
public static class SetNoShowPolicy
{
    /// <summary>Request body.</summary>
    public sealed record Command(bool Enabled, int GracePeriodMinutes);

    /// <summary>FluentValidation rules.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.GracePeriodMinutes).InclusiveBetween(0, 240);
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

        /// <summary>Applies the policy update.</summary>
        public async Task<Result> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            var settings = await _db.TenantSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (settings is null)
            {
                return Result.Failure(TenantErrors.SettingsNotFound);
            }

            var update = settings.SetNoShowPolicy(command.Enabled, command.GracePeriodMinutes, _clock.GetUtcNow());
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

            app.MapPut("/tenants/settings/no-show", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Tenants.SetNoShowPolicy")
                .WithTags("Tenants")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner");
        }
    }
}
