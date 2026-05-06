using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BookSlot.Domain.Services;
using BookSlot.Domain.Staff;
using BookSlot.Domain.ValueObjects;
using BookSlot.Features.ServiceTypes.GetAssignedStaff;
using BookSlot.Features.Shared.Tenancy;
using BookSlot.Infrastructure.Persistence;
using BookSlot.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace BookSlot.IntegrationTests.ServiceTypes;

/// <summary>
/// End-to-end tests for <c>GET /api/v1/service-types/{serviceTypeId}/assigned-staff</c>.
/// Uses a real PostgreSQL container via <see cref="PostgresFixture"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetAssignedStaffEndpointTests : IAsyncLifetime
{
    private const string TenantSlug = "get-assigned-staff-test";
    private const string JwtIssuer = "BookSlot.Tests";
    private const string JwtAudience = "BookSlot.Tests";
    private const string JwtSigningKey = "test-signing-key-at-least-32-chars-xxxx";

    private static readonly Guid TenantId = TenantIdFactory.FromSlug(TenantSlug);

    private readonly PostgresFixture _pgFixture;
    private Factory? _factory;
    private HttpClient? _client;

    public GetAssignedStaffEndpointTests(PostgresFixture pgFixture)
    {
        _pgFixture = pgFixture;
    }

    public async Task InitializeAsync()
    {
        _factory = new Factory(_pgFixture.ConnectionString);
        _client = _factory.CreateClient();

        await using var migrCtx = await _pgFixture.CreateMigratedContextAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_WithAssignedActiveStaff_Returns200WithExpectedItems()
    {
        // Arrange
        var (serviceTypeId, staffId1, staffId2) = await SeedServiceTypeAndAssignedStaffAsync();

        // Act
        using var response = await GetAsync(serviceTypeId, withJwt: true);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetAssignedStaff.Response>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { staffId1, staffId2 });
    }

    [Fact]
    public async Task Get_NoAssignmentsForServiceType_Returns200WithEmptyList()
    {
        // Arrange — service type exists but has no staff assignments
        var serviceTypeId = await SeedServiceTypeWithNoStaffAsync();

        // Act
        using var response = await GetAsync(serviceTypeId, withJwt: true);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetAssignedStaff.Response>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        // Arrange — a valid service type, but no auth token
        var serviceTypeId = await SeedServiceTypeWithNoStaffAsync();

        // Act
        using var response = await GetAsync(serviceTypeId, withJwt: false);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid serviceTypeId, Guid staffId1, Guid staffId2)>
        SeedServiceTypeAndAssignedStaffAsync()
    {
        await using var ctx = CreateSeedContext(TenantId, TenantSlug);

        var serviceTypeId = Guid.NewGuid();
        var slugValue = $"gas-svc-{Guid.NewGuid():N}"[..20];
        var slug = Slug.Create(slugValue).Value;
        var serviceType = ServiceType.Create(serviceTypeId, TenantId, "Get Assigned Staff Test", slug,
            45, 0, 0, 80m, "USD", null, DateTimeOffset.UtcNow).Value;
        ctx.ServiceTypes.Add(serviceType);

        var staffId1 = Guid.NewGuid();
        var staffId2 = Guid.NewGuid();
        var staff1 = StaffMember.Create(staffId1, TenantId, "Alice Tester", "Therapist", null, DateTimeOffset.UtcNow).Value;
        var staff2 = StaffMember.Create(staffId2, TenantId, "Bob Tester", null, null, DateTimeOffset.UtcNow).Value;
        ctx.Staff.Add(staff1);
        ctx.Staff.Add(staff2);

        ctx.StaffServiceAssignments.Add(new StaffServiceAssignment(Guid.NewGuid(), TenantId, staffId1, serviceTypeId));
        ctx.StaffServiceAssignments.Add(new StaffServiceAssignment(Guid.NewGuid(), TenantId, staffId2, serviceTypeId));

        await ctx.SaveChangesAsync();

        return (serviceTypeId, staffId1, staffId2);
    }

    private async Task<Guid> SeedServiceTypeWithNoStaffAsync()
    {
        await using var ctx = CreateSeedContext(TenantId, TenantSlug);

        var serviceTypeId = Guid.NewGuid();
        var slugValue = $"gas-empty-{Guid.NewGuid():N}"[..20];
        var slug = Slug.Create(slugValue).Value;
        var serviceType = ServiceType.Create(serviceTypeId, TenantId, "Empty Staff Service", slug,
            30, 0, 0, 0m, "USD", null, DateTimeOffset.UtcNow).Value;
        ctx.ServiceTypes.Add(serviceType);

        await ctx.SaveChangesAsync();

        return serviceTypeId;
    }

    private Task<HttpResponseMessage> GetAsync(Guid serviceTypeId, bool withJwt)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/service-types/{serviceTypeId}/assigned-staff");

        if (withJwt)
        {
            var jwt = GenerateJwt("Owner");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
        }

        return _client!.SendAsync(request);
    }

    private static string GenerateJwt(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim("tenant_slug", TenantSlug),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        };
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private AppDbContext CreateSeedContext(Guid tenantId, string slug)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pgFixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        var tenant = new CurrentTenantAccessor();
        tenant.Set(tenantId, slug);
        return new AppDbContext(options, tenant);
    }

    // ── WebApplicationFactory ─────────────────────────────────────────────────

    /// <summary>
    /// Overrides the Postgres connection string and JWT auth config so the API
    /// uses the test container instead of the real database.
    /// </summary>
    public sealed class Factory : WebApplicationFactory<BookSlot.Api.Program>
    {
        private readonly string _connectionString;

        public Factory(string connectionString) => _connectionString = connectionString;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _connectionString,
                    ["Auth:Jwt:Issuer"] = JwtIssuer,
                    ["Auth:Jwt:Audience"] = JwtAudience,
                    ["Auth:Jwt:SigningKey"] = JwtSigningKey,
                    ["Auth:Jwt:ApiKeyPepper"] = "test-api-key-pepper-at-least-32-chars-x",
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
