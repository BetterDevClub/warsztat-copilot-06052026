using System.Linq.Expressions;
using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Services;
using BookSlot.Domain.Staff;
using BookSlot.Domain.ValueObjects;
using BookSlot.Features.ServiceTypes.BulkAssignStaff;
using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;

namespace BookSlot.UnitTests.ServiceTypes;

/// <summary>Unit tests for <see cref="BulkAssignStaff.Handler"/>.</summary>
public class BulkAssignStaffHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // ── Helper: stub ICurrentTenant ──────────────────────────────────────────

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid? tenantId) => TenantId = tenantId;
        public bool IsResolved => TenantId.HasValue;
        public Guid? TenantId { get; }
        public string? Slug => null;
    }

    // ── Async query provider helpers (EF Core in-memory async support) ────────

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        internal TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) =>
            new TestAsyncEnumerable<TEntity>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => _inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) =>
            _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult);
            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var innerType = resultType.GetGenericArguments()[0];
                var executionResult = _inner.Execute(expression);
                var fromResult = typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(innerType);
                return (TResult)fromResult.Invoke(null, new[] { executionResult })!;
            }

            throw new NotSupportedException($"Unsupported async result type: {resultType}");
        }
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
        public TestAsyncEnumerable(Expression expression) : base(expression) { }

        // EnumerableQuery<T> implements IQueryable.Provider explicitly, so base.Provider is inaccessible.
        // Build a fresh EnumerableQuery<T> from this query's expression to obtain the provider.
        IQueryProvider IQueryable.Provider =>
            new TestAsyncQueryProvider<T>(
                ((IQueryable<T>)new EnumerableQuery<T>(((IQueryable)this).Expression)).Provider);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;

        public T Current => _inner.Current;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    // ── Helper: create a fake DbSet backed by a list ─────────────────────────

    private static DbSet<T> CreateFakeDbSet<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mockSet = Substitute.For<DbSet<T>, IQueryable<T>, IAsyncEnumerable<T>>();

        ((IQueryable<T>)mockSet).Provider.Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        ((IQueryable<T>)mockSet).Expression.Returns(queryable.Expression);
        ((IQueryable<T>)mockSet).ElementType.Returns(queryable.ElementType);
        ((IQueryable<T>)mockSet).GetEnumerator().Returns(queryable.GetEnumerator());
        ((IAsyncEnumerable<T>)mockSet)
            .GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));

        return mockSet;
    }

    // ── Helper: create a substitute AppDbContext ──────────────────────────────

    private static AppDbContext CreateSubstituteDb(
        ICurrentTenant tenant,
        List<ServiceType>? serviceTypes = null,
        List<StaffMember>? staff = null,
        List<StaffServiceAssignment>? assignments = null,
        List<StaffServiceAssignment>? capturedAdds = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().Options;
        var db = Substitute.For<AppDbContext>(options, tenant);

        var serviceTypeDbSet = CreateFakeDbSet(serviceTypes ?? new List<ServiceType>());
        var staffDbSet = CreateFakeDbSet(staff ?? new List<StaffMember>());
        var assignmentsDbSet = CreateFakeDbSet(assignments ?? new List<StaffServiceAssignment>());

        db.Set<ServiceType>().Returns(serviceTypeDbSet);
        db.Set<StaffMember>().Returns(staffDbSet);
        db.Set<StaffServiceAssignment>().Returns(assignmentsDbSet);

        db.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));

        if (capturedAdds is not null)
        {
            assignmentsDbSet
                .When(x => x.Add(Arg.Any<StaffServiceAssignment>()))
                .Do(call => capturedAdds.Add(call.Arg<StaffServiceAssignment>()));
        }

        return db;
    }

    // ── Helpers to create domain entities ────────────────────────────────────

    private static ServiceType MakeServiceType(Guid id, Guid tenantId)
    {
        var slug = Slug.Create($"svc-{id:N}"[..10]).Value;
        return ServiceType.Create(id, tenantId, "Test Service", slug,
            30, 0, 0, 0m, "USD", null, DateTimeOffset.UtcNow).Value;
    }

    private static StaffMember MakeStaffMember(Guid id, Guid tenantId) =>
        StaffMember.Create(id, tenantId, "Test Staff", null, null, DateTimeOffset.UtcNow).Value;

    private static StaffServiceAssignment MakeAssignment(Guid id, Guid tenantId, Guid staffId, Guid serviceTypeId) =>
        new(id, tenantId, staffId, serviceTypeId);

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NewAssignments_ReturnsCorrectCount()
    {
        // Arrange
        var serviceTypeId = Guid.NewGuid();
        var staffId1 = Guid.NewGuid();
        var staffId2 = Guid.NewGuid();
        var staffId3 = Guid.NewGuid();
        var tenant = new StubCurrentTenant(TenantId);

        var serviceTypes = new List<ServiceType> { MakeServiceType(serviceTypeId, TenantId) };
        var staff = new List<StaffMember>
        {
            MakeStaffMember(staffId1, TenantId),
            MakeStaffMember(staffId2, TenantId),
            MakeStaffMember(staffId3, TenantId),
        };
        // staffId1 is already assigned
        var assignments = new List<StaffServiceAssignment>
        {
            MakeAssignment(Guid.NewGuid(), TenantId, staffId1, serviceTypeId),
        };
        var capturedAdds = new List<StaffServiceAssignment>();

        var db = CreateSubstituteDb(tenant, serviceTypes, staff, assignments, capturedAdds);
        var handler = new BulkAssignStaff.Handler(db, tenant);
        var command = new BulkAssignStaff.Command([staffId1, staffId2, staffId3]);

        // Act
        var result = await handler.HandleAsync(serviceTypeId, command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AssignedCount.Should().Be(2);
        capturedAdds.Should().HaveCount(2);
        capturedAdds.Select(a => a.StaffId).Should().BeEquivalentTo(new[] { staffId2, staffId3 });
    }

    [Fact]
    public async Task Handle_AllAlreadyAssigned_ReturnsZero()
    {
        // Arrange
        var serviceTypeId = Guid.NewGuid();
        var staffId1 = Guid.NewGuid();
        var staffId2 = Guid.NewGuid();
        var tenant = new StubCurrentTenant(TenantId);

        var serviceTypes = new List<ServiceType> { MakeServiceType(serviceTypeId, TenantId) };
        var staff = new List<StaffMember>
        {
            MakeStaffMember(staffId1, TenantId),
            MakeStaffMember(staffId2, TenantId),
        };
        var assignments = new List<StaffServiceAssignment>
        {
            MakeAssignment(Guid.NewGuid(), TenantId, staffId1, serviceTypeId),
            MakeAssignment(Guid.NewGuid(), TenantId, staffId2, serviceTypeId),
        };
        var capturedAdds = new List<StaffServiceAssignment>();

        var db = CreateSubstituteDb(tenant, serviceTypes, staff, assignments, capturedAdds);
        var handler = new BulkAssignStaff.Handler(db, tenant);
        var command = new BulkAssignStaff.Command([staffId1, staffId2]);

        // Act
        var result = await handler.HandleAsync(serviceTypeId, command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AssignedCount.Should().Be(0);
        capturedAdds.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyStaffIds_ReturnsZero()
    {
        // Arrange
        var serviceTypeId = Guid.NewGuid();
        var tenant = new StubCurrentTenant(TenantId);

        var serviceTypes = new List<ServiceType> { MakeServiceType(serviceTypeId, TenantId) };
        var capturedAdds = new List<StaffServiceAssignment>();

        var db = CreateSubstituteDb(tenant, serviceTypes, capturedAdds: capturedAdds);
        var handler = new BulkAssignStaff.Handler(db, tenant);
        var command = new BulkAssignStaff.Command([]);

        // Act
        var result = await handler.HandleAsync(serviceTypeId, command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AssignedCount.Should().Be(0);
        capturedAdds.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateStaffIdsInRequest_DeduplicatesAndCounts()
    {
        // Arrange
        var serviceTypeId = Guid.NewGuid();
        var staffId1 = Guid.NewGuid();
        var tenant = new StubCurrentTenant(TenantId);

        var serviceTypes = new List<ServiceType> { MakeServiceType(serviceTypeId, TenantId) };
        var staff = new List<StaffMember> { MakeStaffMember(staffId1, TenantId) };
        var capturedAdds = new List<StaffServiceAssignment>();

        var db = CreateSubstituteDb(tenant, serviceTypes, staff, capturedAdds: capturedAdds);
        var handler = new BulkAssignStaff.Handler(db, tenant);
        // staffId1 appears twice — should be de-duplicated
        var command = new BulkAssignStaff.Command([staffId1, staffId1]);

        // Act
        var result = await handler.HandleAsync(serviceTypeId, command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AssignedCount.Should().Be(1);
        capturedAdds.Should().HaveCount(1);
        capturedAdds[0].StaffId.Should().Be(staffId1);
    }

    [Fact]
    public async Task Handle_ServiceTypeNotFound_ReturnsNotFound()
    {
        // Arrange
        var serviceTypeId = Guid.NewGuid();
        var staffId1 = Guid.NewGuid();
        var tenant = new StubCurrentTenant(TenantId);

        // Empty service type list — the service type is not found
        var db = CreateSubstituteDb(tenant);
        var handler = new BulkAssignStaff.Handler(db, tenant);
        var command = new BulkAssignStaff.Command([staffId1]);

        // Act
        var result = await handler.HandleAsync(serviceTypeId, command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceType.NotFound");
    }

    [Fact]
    public async Task Handle_StaffNotFound_ReturnsNotFound()
    {
        // Arrange
        var serviceTypeId = Guid.NewGuid();
        var staffId1 = Guid.NewGuid();
        var unknownStaffId = Guid.NewGuid();
        var tenant = new StubCurrentTenant(TenantId);

        var serviceTypes = new List<ServiceType> { MakeServiceType(serviceTypeId, TenantId) };
        // staffDbSet contains only staffId1, NOT unknownStaffId
        var staff = new List<StaffMember> { MakeStaffMember(staffId1, TenantId) };

        var db = CreateSubstituteDb(tenant, serviceTypes, staff);
        var handler = new BulkAssignStaff.Handler(db, tenant);
        // Request includes an id that doesn't exist in the tenant
        var command = new BulkAssignStaff.Command([staffId1, unknownStaffId]);

        // Act
        var result = await handler.HandleAsync(serviceTypeId, command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceType.StaffNotFound");
    }

    [Fact]
    public async Task Handle_TenantUnresolved_ReturnsUnauthorized()
    {
        // Arrange
        var serviceTypeId = Guid.NewGuid();
        var staffId1 = Guid.NewGuid();
        var tenant = new StubCurrentTenant(null); // unresolved

        var db = CreateSubstituteDb(tenant);
        var handler = new BulkAssignStaff.Handler(db, tenant);
        var command = new BulkAssignStaff.Command([staffId1]);

        // Act
        var result = await handler.HandleAsync(serviceTypeId, command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.Unresolved");
    }
}
