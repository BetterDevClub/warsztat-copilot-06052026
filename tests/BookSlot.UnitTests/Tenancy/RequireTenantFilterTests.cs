using BookSlot.Features.Shared.Tenancy;
using Microsoft.AspNetCore.Http;

namespace BookSlot.UnitTests.Tenancy;

public class RequireTenantFilterTests
{
    [Fact]
    public async Task Returns_Problem_When_Tenant_Unresolved()
    {
        var filter = new RequireTenantFilter(new CurrentTenantAccessor());
        var ctx = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());
        var nextCalled = false;

        var result = await filter.InvokeAsync(ctx, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextCalled.Should().BeFalse();
        result.Should().BeAssignableTo<IResult>();
        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Invokes_Next_When_Tenant_Resolved()
    {
        var accessor = new CurrentTenantAccessor();
        accessor.Set(Guid.NewGuid(), "acme");
        var filter = new RequireTenantFilter(accessor);
        var ctx = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());
        var nextCalled = false;

        var result = await filter.InvokeAsync(ctx, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok("pass"));
        });

        nextCalled.Should().BeTrue();
        result.Should().BeAssignableTo<IResult>();
    }
}
