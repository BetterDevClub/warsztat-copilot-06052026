using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using BookSlot.Web.Client;

namespace BookSlot.Web.Auth;

/// <summary>
/// Server-side counterpart to <c>PersistentAuthenticationStateProvider</c>: snapshots
/// the current <see cref="AuthenticationState"/> during SSR and writes a
/// <see cref="UserInfo"/> into <see cref="PersistentComponentState"/> so the WASM
/// client can rehydrate it without a second round-trip to the server.
/// </summary>
internal sealed class PersistingServerAuthenticationStateProvider : ServerAuthenticationStateProvider, IDisposable
{
    private readonly PersistentComponentState _state;
    private readonly PersistingComponentStateSubscription _subscription;
    private Task<AuthenticationState>? _authenticationStateTask;

    public PersistingServerAuthenticationStateProvider(PersistentComponentState state)
    {
        _state = state;
        AuthenticationStateChanged += OnAuthenticationStateChanged;
        _subscription = state.RegisterOnPersisting(OnPersistingAsync, Microsoft.AspNetCore.Components.Web.RenderMode.InteractiveWebAssembly);
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task) => _authenticationStateTask = task;

    private async Task OnPersistingAsync()
    {
        if (_authenticationStateTask is null)
        {
            return;
        }

        var authState = await _authenticationStateTask.ConfigureAwait(false);
        var principal = authState.User;
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return;
        }

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.Identity.Name;
        if (userId is null || email is null)
        {
            return;
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        _state.PersistAsJson(nameof(UserInfo), new UserInfo(userId, email, roles));
    }

    public void Dispose()
    {
        _subscription.Dispose();
        AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }
}
