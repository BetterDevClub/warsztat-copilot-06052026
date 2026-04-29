using BookSlot.Features.Shared.Tenancy;
using BookSlot.Infrastructure.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BookSlot.Web.Auth;

/// <summary>
/// Resolves the current tenant for each Blazor Server circuit by reading the
/// <c>tenant_slug</c> claim from the authenticated user's principal.
/// <c>TenantResolutionMiddleware</c> only runs on HTTP requests — SignalR circuit
/// messages bypass the middleware pipeline entirely. This handler bridges the gap
/// so every <see cref="BookSlot.Domain.Abstractions.ICurrentTenant"/> consumer
/// inside a circuit (slice handlers, DbContext query filters) sees a resolved tenant.
/// </summary>
internal sealed class TenantCircuitHandler : CircuitHandler
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly CurrentTenantAccessor _accessor;
    private readonly ILogger<TenantCircuitHandler> _logger;

    /// <summary>Creates the handler. All dependencies come from the circuit's DI scope.</summary>
    public TenantCircuitHandler(
        AuthenticationStateProvider authStateProvider,
        CurrentTenantAccessor accessor,
        ILogger<TenantCircuitHandler> logger)
    {
        _authStateProvider = authStateProvider;
        _accessor = accessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var authState = await _authStateProvider
            .GetAuthenticationStateAsync()
            .ConfigureAwait(false);

        var slug = authState.User.FindFirst(JwtTokenGenerator.TenantSlugClaim)?.Value;

        if (!string.IsNullOrWhiteSpace(slug))
        {
            _accessor.Set(TenantIdFactory.FromSlug(slug), slug);
            _logger.LogDebug("Circuit {CircuitId}: resolved tenant '{Slug}'.", circuit.Id, slug);
        }
        else
        {
            _logger.LogDebug(
                "Circuit {CircuitId}: no tenant_slug claim found — tenant remains unresolved.",
                circuit.Id);
        }
    }
}
