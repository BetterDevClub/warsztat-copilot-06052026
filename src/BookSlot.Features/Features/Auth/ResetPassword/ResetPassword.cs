using System.Text;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace BookSlot.Features.Auth.ResetPassword;

/// <summary>
/// Consumes a reset token (issued by <see cref="RequestPasswordReset"/>) and sets a
/// new password. Public endpoint — the token is the authoritative proof of ownership.
/// </summary>
public static class ResetPassword
{
    /// <summary>Request body.</summary>
    public sealed record Command(Guid UserId, string Token, string NewPassword);

    /// <summary>FluentValidation rules.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.UserId).NotEmpty();
            RuleFor(x => x.Token).NotEmpty().MaximumLength(4096);
            RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(256);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly UserManager<ApplicationUser> _users;

        /// <summary>Creates a new handler.</summary>
        public Handler(UserManager<ApplicationUser> users)
        {
            _users = users;
        }

        /// <summary>Resets the password.</summary>
        public async Task<Result> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            _ = cancellationToken;

            var user = await _users.FindByIdAsync(command.UserId.ToString()).ConfigureAwait(false);
            if (user is null)
            {
                return Result.Failure(AuthErrors.UserNotFound);
            }

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Token));
            }
            catch (FormatException)
            {
                return Result.Failure(AuthErrors.InvalidToken);
            }

            var identityResult = await _users.ResetPasswordAsync(user, decoded, command.NewPassword).ConfigureAwait(false);
            return identityResult.Succeeded
                ? Result.Success()
                : Result.Failure(AuthErrors.InvalidToken);
        }
    }

    /// <summary>Endpoint registration — public (no tenant, no auth).</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.Public;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.MapPost("/auth/reset-password", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Auth.ResetPassword")
                .WithTags("Auth")
                .WithValidation<Command>()
                .AllowAnonymous();
        }
    }
}
