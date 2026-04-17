using BookSlot.Domain.Primitives;
using BookSlot.Domain.Tenants;
using BookSlot.Domain.ValueObjects;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Identity;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookSlot.Features.Tenants.Register;

/// <summary>
/// Public self-service onboarding: creates a new <see cref="Tenant"/>, its first
/// <c>Owner</c> user, and a default <see cref="TenantSettings"/> row — all in a single
/// transaction. Caller is expected to hit <c>/auth/login</c> afterwards to obtain tokens.
/// </summary>
public static class RegisterTenant
{
    /// <summary>Request body.</summary>
    public sealed record Command(
        string Slug,
        string Name,
        string OwnerEmail,
        string OwnerPassword,
        string? TimeZoneId);

    /// <summary>Response payload — echoes the identifiers so the client can proceed to login.</summary>
    public sealed record Response(Guid TenantId, string Slug, string Name, Guid OwnerUserId);

    /// <summary>FluentValidation rules.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.Slug).NotEmpty().MaximumLength(63);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.OwnerPassword).NotEmpty().MinimumLength(8).MaximumLength(256);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, UserManager<ApplicationUser> users, TimeProvider clock)
        {
            _db = db;
            _users = users;
            _clock = clock;
        }

        /// <summary>Executes the onboarding flow inside a single transaction.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            var slugResult = TenantSlug.Create(command.Slug);
            if (slugResult.IsFailure)
            {
                return Result.Failure<Response>(slugResult.Error);
            }

            if (await _db.Tenants.AnyAsync(t => t.Slug == slugResult.Value.Value, cancellationToken).ConfigureAwait(false))
            {
                return Result.Failure<Response>(TenantErrors.SlugTaken);
            }

            var now = _clock.GetUtcNow();
            var tenantId = Guid.NewGuid();
            var tenantResult = Tenant.Create(tenantId, slugResult.Value, command.Name, now);
            if (tenantResult.IsFailure)
            {
                return Result.Failure<Response>(tenantResult.Error);
            }

            var settings = TenantSettings.CreateDefault(tenantId);
            if (!string.IsNullOrWhiteSpace(command.TimeZoneId))
            {
                var seed = settings.Update(command.TimeZoneId, 30, null, null, null, now);
                if (seed.IsFailure)
                {
                    return Result.Failure<Response>(seed.Error);
                }
            }

            await using IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            _db.Tenants.Add(tenantResult.Value);
            _db.TenantSettings.Add(settings);

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserName = command.OwnerEmail,
                Email = command.OwnerEmail,
                EmailConfirmed = true, // auto-confirm the registrant — they proved ownership by picking the password.
                CreatedAt = now,
            };

            var createResult = await _users.CreateAsync(user, command.OwnerPassword).ConfigureAwait(false);
            if (!createResult.Succeeded)
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result.Failure<Response>(Error.Validation(
                    "Tenants.Register.UserCreationFailed",
                    string.Join("; ", createResult.Errors.Select(e => e.Description))));
            }

            var roleResult = await _users.AddToRoleAsync(user, Domain.Abstractions.Roles.Owner).ConfigureAwait(false);
            if (!roleResult.Succeeded)
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result.Failure<Response>(Error.Failure(
                    "Tenants.Register.RoleAssignFailed",
                    string.Join("; ", roleResult.Errors.Select(e => e.Description))));
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new Response(tenantId, tenantResult.Value.Slug, tenantResult.Value.Name, user.Id));
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

            app.MapPost("/tenants/register", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status201Created);
                })
                .WithName("Tenants.Register")
                .WithTags("Tenants")
                .WithValidation<Command>()
                .AllowAnonymous()
                .Produces<Response>(StatusCodes.Status201Created);
        }
    }
}
