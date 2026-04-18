using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BookSlot.Web.Client;

/// <summary>
/// Client-side <see cref="AuthenticationStateProvider"/> that rehydrates the user
/// principal from <see cref="PersistentComponentState"/>. The server writes a
/// <see cref="UserInfo"/> payload during SSR (see <c>PersistentAuthenticationStateProvider</c>
/// registration in the Web host); the WASM client reads it on startup and thereafter
/// returns the cached state. Re-login requires a full page navigation so the server can
/// refresh the payload.
/// </summary>
internal sealed class PersistentAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> Unauthenticated =
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

    private readonly Task<AuthenticationState> _state;

    public PersistentAuthenticationStateProvider(PersistentComponentState persistent)
    {
        if (!persistent.TryTakeFromJson<UserInfo>(nameof(UserInfo), out var userInfo) || userInfo is null)
        {
            _state = Unauthenticated;
            return;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userInfo.UserId),
            new(ClaimTypes.Name, userInfo.Email),
            new(ClaimTypes.Email, userInfo.Email),
        };
        claims.AddRange(userInfo.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, authenticationType: "PersistentAuthenticationState");
        _state = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state;
}

/// <summary>Payload persisted by the server host and rehydrated on the WASM client.</summary>
public sealed record UserInfo(string UserId, string Email, IReadOnlyList<string> Roles);
