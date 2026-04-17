using BookSlot.Domain.Primitives;

namespace BookSlot.Features.Auth;

/// <summary>Canonical <see cref="Error"/> values returned from auth slices. Kept internal to the Auth feature folder.</summary>
internal static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "Email or password is incorrect.");

    public static readonly Error AccountLocked =
        Error.Unauthorized("Auth.AccountLocked", "The account is temporarily locked. Try again later.");

    public static readonly Error EmailNotConfirmed =
        Error.Unauthorized("Auth.EmailNotConfirmed", "Confirm your email address before signing in.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("Auth.InvalidRefreshToken", "The refresh token is invalid or has already been used.");

    public static readonly Error UserNotFound =
        Error.NotFound("Auth.UserNotFound", "No user matches the provided identifier.");

    public static readonly Error InvalidToken =
        Error.Validation("Auth.InvalidToken", "The token is invalid or has expired.");
}
