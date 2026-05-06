using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BookSlot.Domain.Services;
using BookSlot.Domain.Staff;
using BookSlot.Domain.ValueObjects;
using BookSlot.Features.ServiceTypes.BulkAssignStaff;
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
/// End-to-end tests for <c>POST /api/v1/service-types/{serviceTypeId}/bulk-assign-staff</c>.
/// Uses a real PostgreSQL container via <see cref="PostgresFixture"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BulkAssignStaffEndpointTests : IAsyncLifetime
{
    private const string TenantSlug = "bulk-assign-test";
    private const string JwtIssuer = "BookSlot.Tests";
    private const string JwtAudience = "BookSlot.Tests";
    private const string JwtSigningKey = "test-signing-key-at-least-32-chars-xxxx";

    private static readonly Guid TenantId = TenantIdFactory.FromSlug(TenantSlug);

    private readonly PostgresFixture _pgFixture;
    private Factory? _factory;
    private HttpClient? _client;

    public BulkAssignStaffEndpointTests(PostgresFixture pgFixture)
    {
        _pgFixture = pgFixture;
    }

    public async Task InitializeAsync()
    {
        _factory = new Factory(_pgFixture.ConnectionString);
        _client = _factory.CreateClient();

        // Ensure migrations are applied using the correctly-configured context
        // (uses npgsql.MigrationsHistoryTable("__ef_migrations_history") via AddPersistence)
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
    public async Task Post_ValidRequest_Returns200WithAssignedCount()
    {
        // Arrange
        var (serviceTypeId, staffId1, staffId2) = await SeedServiceTypeAndTwoStaffAsync();

        var body = new { staffIds = new[] { staffId1, staffId2 } };

        // Act
        using var response = await PostAsync(serviceTypeId, body, ownerJwt: true);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkAssignStaff.Response>();
        result.Should().NotBeNull();
        result!.AssignedCount.Should().Be(2);
    }

    [Fact]
    public async Task Post_AlreadyAssigned_SkipsAndReturns200WithZero()
    {
        // Arrange — seed service type, 2 staff, and pre-existing assignments for both
        var (serviceTypeId, staffId1, staffId2) = await SeedServiceTypeAndTwoStaffAsync(
            preAssignBoth: true);

        var body = new { staffIds = new[] { staffId1, staffId2 } };

        // Act
        using var response = await PostAsync(serviceTypeId, body, ownerJwt: true);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkAssignStaff.Response>();
        result.Should().NotBeNull();
        result!.AssignedCount.Should().Be(0);
    }

    [Fact]
    public async Task Post_ServiceTypeNotFound_Returns404()
    {
        // Arrange — unknown service type id (not seeded)
        var unknownId = Guid.NewGuid();
        var body = new { staffIds = new[] { Guid.NewGuid() } };

        // Act
        using var response = await PostAsync(unknownId, body, ownerJwt: true);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_StaffIdNotFound_Returns404()
    {
        // Arrange — service type exists, but one staffId belongs to another tenant
        await using var ctx = CreateSeedContext(TenantId, TenantSlug);

        var serviceTypeId = Guid.NewGuid();
        var slugValue = $"bulk-st-{Guid.NewGuid():N}"[..20];
        var slug = Slug.Create(slugValue).Value;
        var serviceType = ServiceType.Create(serviceTypeId, TenantId, "Bulk 404 Test", slug,
            30, 0, 0, 0m, "USD", null, DateTimeOffset.UtcNow).Value;
        ctx.ServiceTypes.Add(serviceType);

        // Seed a staff member belonging to ANOTHER tenant
        var otherTenantId = TenantIdFactory.FromSlug("other-tenant-bulk");
        var foreignStaffId = Guid.NewGuid();
        var foreignStaff = StaffMember.Create(foreignStaffId, otherTenantId, "Foreign Staff",
            null, null, DateTimeOffset.UtcNow).Value;
        ctx.Staff.Add(foreignStaff);

        await ctx.SaveChangesAsync();

        var body = new { staffIds = new[] { foreignStaffId } };

        // Act
        using var response = await PostAsync(serviceTypeId, body, ownerJwt: true);

        // Assert — global query filter hides the foreign staff → StaffNotFound → 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_NullStaffIds_Returns400()
    {
        // Arrange — body missing staffIds entirely (will bind as null)
        var (serviceTypeId, _, _) = await SeedServiceTypeAndTwoStaffAsync();
        var body = new { }; // no staffIds field

        // Act
        using var response = await PostAsync(serviceTypeId, body, ownerJwt: true);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_StaffRoleForbidden_Returns403()
    {
        // Arrange — authenticate as Staff role (not Owner)
        var (serviceTypeId, staffId1, _) = await SeedServiceTypeAndTwoStaffAsync();
        var body = new { staffIds = new[] { staffId1 } };

        // Act — use Staff JWT (no Owner role)
        using var response = await PostAsync(serviceTypeId, body, ownerJwt: false);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid serviceTypeId, Guid staffId1, Guid staffId2)>
        SeedServiceTypeAndTwoStaffAsync(bool preAssignBoth = false)
    {
        await using var ctx = CreateSeedContext(TenantId, TenantSlug);

        var serviceTypeId = Guid.NewGuid();
        var slugValue = $"bulk-svc-{Guid.NewGuid():N}"[..20];
        var slug = Slug.Create(slugValue).Value;
        var serviceType = ServiceType.Create(serviceTypeId, TenantId, "Bulk Test Service", slug,
            30, 0, 0, 50m, "USD", null, DateTimeOffset.UtcNow).Value;
        ctx.ServiceTypes.Add(serviceType);

        var staffId1 = Guid.NewGuid();
        var staffId2 = Guid.NewGuid();
        var staff1 = StaffMember.Create(staffId1, TenantId, "Bulk Staff One", null, null, DateTimeOffset.UtcNow).Value;
        var staff2 = StaffMember.Create(staffId2, TenantId, "Bulk Staff Two", null, null, DateTimeOffset.UtcNow).Value;
        ctx.Staff.Add(staff1);
        ctx.Staff.Add(staff2);

        if (preAssignBoth)
        {
            ctx.StaffServiceAssignments.Add(
                new StaffServiceAssignment(Guid.NewGuid(), TenantId, staffId1, serviceTypeId));
            ctx.StaffServiceAssignments.Add(
                new StaffServiceAssignment(Guid.NewGuid(), TenantId, staffId2, serviceTypeId));
        }

        await ctx.SaveChangesAsync();

        return (serviceTypeId, staffId1, staffId2);
    }

    private Task<HttpResponseMessage> PostAsync(Guid serviceTypeId, object body, bool ownerJwt)
    {
        var jwt = ownerJwt ? GenerateJwt("Owner") : GenerateJwt("Staff");
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/service-types/{serviceTypeId}/bulk-assign-staff");
        request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
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
