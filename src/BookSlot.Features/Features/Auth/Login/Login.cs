using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Identity;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Infrastructure.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookSlot.Features.Auth.Login;

/// <summary>
/// Authenticates a tenant user with email + password and returns a pair of access +
/// refresh tokens. Tenant context is resolved by the middleware before this slice runs
/// (<c>/api/v1</c> group) so user lookup is scoped to the caller's tenant.
/// </summary>
public static class Login
{
    /// <summary>Request body.</summary>
    public sealed record Command(string Email, string Password);

    /// <summary>Response payload returned to authenticated callers.</summary>
    public sealed record Response(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

    /// <summary>FluentValidation rules.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator instance.</summary>
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly ICurrentTenant _tenant;
        private readonly IJwtTokenGenerator _jwt;
        private readonly AppDbContext _db;
        private readonly IOptions<JwtOptions> _jwtOptions;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(
            UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn,
            ICurrentTenant tenant,
            IJwtTokenGenerator jwt,
            AppDbContext db,
            IOptions<JwtOptions> jwtOptions,
            TimeProvider clock)
        {
            _users = users;
            _signIn = signIn;
            _tenant = tenant;
            _jwt = jwt;
            _db = db;
            _jwtOptions = jwtOptions;
            _clock = clock;
        }

        /// <summary>Executes the login flow.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            var tenantId = _tenant.TenantId!.Value;
            var normalizedEmail = _users.NormalizeEmail(command.Email);
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.NormalizedEmail == normalizedEmail, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                return Result.Failure<Response>(AuthErrors.InvalidCredentials);
            }

            // Re-load through UserManager so lockout/access counters work.
            var managed = await _users.FindByIdAsync(user.Id.ToString()).ConfigureAwait(false);
            if (managed is null)
            {
                return Result.Failure<Response>(AuthErrors.InvalidCredentials);
            }

            var signInResult = await _signIn.CheckPasswordSignInAsync(managed, command.Password, lockoutOnFailure: true)
                .ConfigureAwait(false);

            if (signInResult.IsLockedOut)
            {
                return Result.Failure<Response>(AuthErrors.AccountLocked);
            }
            if (signInResult.IsNotAllowed)
            {
                return Result.Failure<Response>(AuthErrors.EmailNotConfirmed);
            }
            if (!signInResult.Succeeded)
            {
                return Result.Failure<Response>(AuthErrors.InvalidCredentials);
            }

            var roles = await _users.GetRolesAsync(managed).ConfigureAwait(false);
            var access = _jwt.CreateAccessToken(managed, _tenant.Slug!, roles);

            var rawRefresh = TokenHasher.NewOpaqueToken();
            var now = _clock.GetUtcNow();
            var refresh = new RefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TenantSlug = _tenant.Slug!,
                UserId = managed.Id,
                TokenHash = TokenHasher.HashRefreshToken(rawRefresh),
                CreatedAt = now,
                ExpiresAt = now.Add(_jwtOptions.Value.RefreshTokenLifetime),
            };
            _db.RefreshTokens.Add(refresh);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new Response(
                access.Value, access.ExpiresAt,
                rawRefresh, refresh.ExpiresAt));
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

            app.MapPost("/auth/login", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Auth.Login")
                .WithTags("Auth")
                .WithValidation<Command>()
                .AllowAnonymous()
                .Produces<Response>();
        }
    }
}
