using System.Net;
using System.Net.Http.Json;
using BookSlot.Features.Diagnostics.WhoAmI;
using BookSlot.Features.Shared.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BookSlot.IntegrationTests.Tenancy;

/// <summary>
/// End-to-end check that the tenant resolution middleware + /api/v1 route group +
/// <see cref="RequireTenantFilter"/> behave correctly against the real API host.
/// No database is touched — the endpoint only reads from <see cref="BookSlot.Domain.Abstractions.ICurrentTenant"/>.
/// </summary>
public class TenantResolutionEndpointTests : IClassFixture<TenantResolutionEndpointTests.Factory>
{
    private readonly Factory _factory;

    public TenantResolutionEndpointTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TenantScoped_Endpoint_Returns_400_When_Tenant_Not_Provided()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/v1/diagnostics/whoami", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TenantScoped_Endpoint_Returns_Resolved_Tenant_From_Header()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostics/whoami");
        request.Headers.Add("X-Tenant-Slug", "acme");

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<WhoAmI.Response>();
        body.Should().NotBeNull();
        body!.Slug.Should().Be("acme");
        body.TenantId.Should().Be(TenantIdFactory.FromSlug("acme"));
    }

    [Fact]
    public async Task Public_Endpoint_Is_Reachable_Without_Tenant()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/ping", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// WebApplicationFactory that overrides the <c>Postgres</c> connection string with an
    /// unreachable stub. EF does not connect during startup, and the diagnostic endpoint
    /// never opens a database connection — so the stub is never dialled.
    /// </summary>
    public sealed class Factory : WebApplicationFactory<BookSlot.Api.Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"]
                        = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
                });
            });
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
        }
    }
}
