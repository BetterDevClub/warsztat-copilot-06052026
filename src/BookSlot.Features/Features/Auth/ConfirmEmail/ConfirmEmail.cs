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

namespace BookSlot.Features.Auth.ConfirmEmail;

/// <summary>
/// Confirms a user's email using the token produced by <see cref="UserManager{TUser}.GenerateEmailConfirmationTokenAsync(TUser)"/>.
/// Public endpoint — link is emailed to the user; the token is the authoritative proof of ownership.
/// </summary>
public static class ConfirmEmail
{
    /// <summary>Query parameters.</summary>
    public sealed record Command(Guid UserId, string Token);

    /// <summary>FluentValidation rules.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.UserId).NotEmpty();
            RuleFor(x => x.Token).NotEmpty().MaximumLength(4096);
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

        /// <summary>Confirms the email and unlocks sign-in.</summary>
        public async Task<Result> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            _ = cancellationToken;

            var user = await _users.FindByIdAsync(command.UserId.ToString()).ConfigureAwait(false);
            if (user is null)
            {
                return Result.Failure(AuthErrors.UserNotFound);
            }

            // Token arrives base64url-encoded in the email link; decode before handing to Identity.
            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Token));
            }
            catch (FormatException)
            {
                return Result.Failure(AuthErrors.InvalidToken);
            }

            var identityResult = await _users.ConfirmEmailAsync(user, decoded).ConfigureAwait(false);
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

            app.MapPost("/auth/confirm-email", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Auth.ConfirmEmail")
                .WithTags("Auth")
                .WithValidation<Command>()
                .AllowAnonymous();
        }
    }
}
