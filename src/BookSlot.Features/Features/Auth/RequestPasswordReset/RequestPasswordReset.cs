using System.Text;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Emailing;
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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Auth.RequestPasswordReset;

/// <summary>
/// Issues a password reset token and emails it to the user. The response is always
/// 204 — we do not reveal whether the email address exists. Tenant context is required
/// (same email may exist in multiple tenants).
/// </summary>
public static class RequestPasswordReset
{
    /// <summary>Request body.</summary>
    public sealed record Command(string Email);

    /// <summary>FluentValidation rules.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IEmailSender _email;
        private readonly BookSlot.Domain.Abstractions.ICurrentTenant _tenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, UserManager<ApplicationUser> users, IEmailSender email, BookSlot.Domain.Abstractions.ICurrentTenant tenant)
        {
            _db = db;
            _users = users;
            _email = email;
            _tenant = tenant;
        }

        /// <summary>Produces a reset link and emails it. Always returns success.</summary>
        public async Task<Result> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (_tenant.TenantId is null)
                return Result.Failure(Error.Unauthorized("Tenant.Unresolved", "Current tenant could not be resolved."));
            var tenantId = _tenant.TenantId.Value;
            var normalized = _users.NormalizeEmail(command.Email);
            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.NormalizedEmail == normalized, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                // Silently succeed — don't leak which emails exist.
                return Result.Success();
            }

            var token = await _users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var body = $"""
                <p>Use the following token to reset your password (valid for 1 hour):</p>
                <pre>{encoded}</pre>
                <p>User id: {user.Id}</p>
                <p>Tenant: {_tenant.Slug}</p>
                """;
            await _email.SendAsync(new EmailMessage(user.Email!, "Reset your BookSlot password", body), cancellationToken)
                .ConfigureAwait(false);

            return Result.Success();
        }
    }

    /// <summary>Endpoint registration — tenant-scoped, anonymous.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.TenantScoped;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.MapPost("/auth/request-password-reset",
                    async (Command command, Handler handler, CancellationToken ct) =>
                    {
                        var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                        return result.ToHttpResult(StatusCodes.Status204NoContent);
                    })
                .WithName("Auth.RequestPasswordReset")
                .WithTags("Auth")
                .WithValidation<Command>()
                .RequireRateLimiting("auth-sensitive")
                .AllowAnonymous();
        }
    }
}
