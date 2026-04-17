using System.Security.Claims;
using BookSlot.Features.Shared.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookSlot.UnitTests.Tenancy;

public class TenantResolutionMiddlewareTests
{
    private static (TenantResolutionMiddleware Sut, CurrentTenantAccessor Accessor) CreateSut(
        Action<TenantResolutionOptions>? configure = null)
    {
        var options = new TenantResolutionOptions();
        configure?.Invoke(options);
        var accessor = new CurrentTenantAccessor();
        var sut = new TenantResolutionMiddleware(
            accessor,
            Options.Create(options),
            NullLogger<TenantResolutionMiddleware>.Instance);
        return (sut, accessor);
    }

    private static HttpContext CreateContext(string host, Action<HttpContext>? configure = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        configure?.Invoke(ctx);
        return ctx;
    }

    [Fact]
    public async Task Resolves_From_Header_When_Subdomain_And_Claim_Missing()
    {
        var (sut, accessor) = CreateSut();
        var ctx = CreateContext("localhost", c => c.Request.Headers["X-Tenant-Slug"] = "acme");

        await sut.InvokeAsync(ctx, _ => Task.CompletedTask);

        accessor.IsResolved.Should().BeTrue();
        accessor.Slug.Should().Be("acme");
        accessor.TenantId.Should().Be(TenantIdFactory.FromSlug("acme"));
    }

    [Fact]
    public async Task Resolves_From_Subdomain_When_Host_Matches_RootDomain()
    {
        var (sut, accessor) = CreateSut(o => o.RootDomains.Add("bookslot.app"));
        var ctx = CreateContext("acme.bookslot.app");

        await sut.InvokeAsync(ctx, _ => Task.CompletedTask);

        accessor.Slug.Should().Be("acme");
    }

    [Fact]
    public async Task Ignores_Reserved_Subdomain_And_Falls_Back_To_Header()
    {
        var (sut, accessor) = CreateSut(o => o.RootDomains.Add("bookslot.app"));
        var ctx = CreateContext(
            "www.bookslot.app",
            c => c.Request.Headers["X-Tenant-Slug"] = "globex");

        await sut.InvokeAsync(ctx, _ => Task.CompletedTask);

        accessor.Slug.Should().Be("globex");
    }

    [Fact]
    public async Task Claim_Takes_Priority_Over_Header()
    {
        var (sut, accessor) = CreateSut();
        var ctx = CreateContext(
            "localhost",
            c =>
            {
                c.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("tenant_slug", "acme")],
                    authenticationType: "test"));
                c.Request.Headers["X-Tenant-Slug"] = "globex";
            });

        await sut.InvokeAsync(ctx, _ => Task.CompletedTask);

        accessor.Slug.Should().Be("acme");
    }

    [Fact]
    public async Task Leaves_Accessor_Unresolved_When_No_Source_Matches()
    {
        var (sut, accessor) = CreateSut();
        var ctx = CreateContext("localhost");

        await sut.InvokeAsync(ctx, _ => Task.CompletedTask);

        accessor.IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_Invalid_Slug_In_Header()
    {
        var (sut, accessor) = CreateSut();
        var ctx = CreateContext("localhost", c => c.Request.Headers["X-Tenant-Slug"] = "ab cd!");

        await sut.InvokeAsync(ctx, _ => Task.CompletedTask);

        accessor.IsResolved.Should().BeFalse();
    }
}
