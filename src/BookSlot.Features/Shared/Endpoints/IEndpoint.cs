using Microsoft.AspNetCore.Routing;

namespace BookSlot.Features.Shared.Endpoints;

/// <summary>
/// Contract implemented by every VSA slice. One implementation per endpoint;
/// the host auto-discovers implementations by reflection and calls
/// <see cref="MapEndpoint"/> during startup.
/// </summary>
public interface IEndpoint
{
    /// <summary>Registers the endpoint on the given route builder.</summary>
    void MapEndpoint(IEndpointRouteBuilder app);
}
