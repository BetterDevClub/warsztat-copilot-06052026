using System.Collections;
using System.Linq.Expressions;
using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Bookings;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Bookings;
using BookSlot.Features.Bookings.AddNote;
using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace BookSlot.UnitTests.Bookings.AddNote;

public class AddBookingNoteHandlerTests
{
    [Fact]
    public async Task Happy_path_persists_note_and_returns_id_and_createdAt()
    {
        var tenantId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 5, 4, 14, 32, 46, TimeSpan.Zero);

        var db = new TestAppDbContext(new TestCurrentTenant(tenantId));
        var booking = CreateBooking(tenantId, bookingId);
        db.Bookings.Add(booking);

        var handler = new AddBookingNote.Handler(db, new FixedTimeProvider(now), new TestCurrentUser(authorId));

        var result = await handler.HandleAsync(bookingId, new AddBookingNote.Command("  hello  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NoteId.Should().NotBeEmpty();
        result.Value.CreatedAt.Should().Be(now);

        db.BookingNotes.Entities.Should().ContainSingle(n => n.Id == result.Value.NoteId);
        var note = db.BookingNotes.Entities.Single(n => n.Id == result.Value.NoteId);
        note.TenantId.Should().Be(tenantId);
        note.BookingId.Should().Be(bookingId);
        note.AuthorId.Should().Be(authorId);
        note.Content.Should().Be("hello");
        note.CreatedAt.Should().Be(now);
    }

    [Fact]
    public async Task Booking_not_found_returns_feature_error()
    {
        var tenantId = Guid.NewGuid();
        var db = new TestAppDbContext(new TestCurrentTenant(tenantId));

        var handler = new AddBookingNote.Handler(db, new FixedTimeProvider(DateTimeOffset.UtcNow), new TestCurrentUser(Guid.NewGuid()));

        var result = await handler.HandleAsync(Guid.NewGuid(), new AddBookingNote.Command("hello"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingFeatureErrors.BookingNotFound);
    }

    [Fact]
    public async Task Notes_limit_reached_returns_feature_error()
    {
        var tenantId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        var db = new TestAppDbContext(new TestCurrentTenant(tenantId));
        db.Bookings.Add(CreateBooking(tenantId, bookingId));

        for (var i = 0; i < BookingNote.MaxNotesPerBooking; i++)
        {
            var note = BookingNote.Create(
                Guid.NewGuid(),
                tenantId,
                bookingId,
                authorId,
                $"note {i}",
                DateTimeOffset.UtcNow,
                existingNotesCount: i).Value;

            db.BookingNotes.Add(note);
        }

        var handler = new AddBookingNote.Handler(db, new FixedTimeProvider(DateTimeOffset.UtcNow), new TestCurrentUser(authorId));

        var result = await handler.HandleAsync(bookingId, new AddBookingNote.Command("hello"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingFeatureErrors.NotesLimitReached);
    }

    [Fact]
    public async Task Author_unknown_returns_unauthorized_error()
    {
        var tenantId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();

        var db = new TestAppDbContext(new TestCurrentTenant(tenantId));
        db.Bookings.Add(CreateBooking(tenantId, bookingId));

        var handler = new AddBookingNote.Handler(db, new FixedTimeProvider(DateTimeOffset.UtcNow), new TestCurrentUser(userId: null));

        var result = await handler.HandleAsync(bookingId, new AddBookingNote.Command("hello"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BookingNote.AuthorUnknown");
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    private static Booking CreateBooking(Guid tenantId, Guid bookingId)
    {
        var result = Booking.Create(
            bookingId,
            tenantId,
            staffId: Guid.NewGuid(),
            serviceTypeId: Guid.NewGuid(),
            startUtc: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            endUtc: new DateTimeOffset(2026, 5, 4, 10, 30, 0, TimeSpan.Zero),
            guestName: "Guest",
            guestEmail: "guest@example.com",
            guestPhone: null,
            guestNotes: null,
            rescheduledFromId: null,
            now: new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero));

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(Guid? userId)
        {
            UserId = userId;
        }

        public bool IsAuthenticated => UserId is not null;

        public Guid? UserId { get; }

        public string? Email => null;

        public IReadOnlyCollection<string> Roles => Array.Empty<string>();

        public bool IsInRole(string role) => false;
    }

    private sealed record TestCurrentTenant(Guid Id) : ICurrentTenant
    {
        public bool IsResolved => true;

        public Guid? TenantId => Id;

        public string? Slug => "test";
    }

    private sealed class TestAppDbContext : AppDbContext
    {
        public TestAppDbContext(ICurrentTenant currentTenant)
            : base(new DbContextOptionsBuilder<AppDbContext>().Options, currentTenant)
        {
        }

        public new FakeDbSet<Booking> Bookings { get; } = new();

        public new FakeDbSet<BookingNote> BookingNotes { get; } = new();

        public override DbSet<TEntity> Set<TEntity>()
        {
            if (typeof(TEntity) == typeof(Booking))
                return (DbSet<TEntity>)(object)Bookings;

            if (typeof(TEntity) == typeof(BookingNote))
                return (DbSet<TEntity>)(object)BookingNotes;

            throw new NotSupportedException($"TestAppDbContext does not support DbSet<{typeof(TEntity).Name}>.");
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }

    private sealed class FakeDbSet<TEntity> : DbSet<TEntity>, IQueryable<TEntity>, IAsyncEnumerable<TEntity>
        where TEntity : class
    {
        private readonly List<TEntity> _entities = [];

        public IReadOnlyList<TEntity> Entities => _entities;

        public override EntityEntry<TEntity> Add(TEntity entity)
        {
            _entities.Add(entity);
            return null!;
        }

        public override ValueTask<EntityEntry<TEntity>> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            _entities.Add(entity);
            return new ValueTask<EntityEntry<TEntity>>(result: null!);
        }

        public override IEntityType EntityType =>
            throw new NotSupportedException("FakeDbSet does not support EntityType.");

        public Type ElementType => Query.ElementType;

        public Expression Expression => Query.Expression;

        public IQueryProvider Provider => new TestAsyncQueryProvider<TEntity>(Query.Provider);

        private IQueryable<TEntity> Query => _entities.AsQueryable();

        public IEnumerator<TEntity> GetEnumerator() => _entities.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public new IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new TestAsyncEnumerator<TEntity>(_entities.GetEnumerator());
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;

        public T Current => _inner.Current;

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync() => new(_inner.MoveNext());
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        public TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => _inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments().Single();

            var executeMethod = typeof(IQueryProvider).GetMethods()
                .Single(m => m.Name == nameof(IQueryProvider.Execute)
                    && m.IsGenericMethod
                    && m.GetParameters().Length == 1);

            var executionResult = executeMethod
                .MakeGenericMethod(resultType)
                .Invoke(_inner, [expression]);

            var fromResultMethod = typeof(Task).GetMethods()
                .Single(m => m.Name == nameof(Task.FromResult)
                    && m.IsGenericMethod
                    && m.GetParameters().Length == 1);

            return (TResult)fromResultMethod
                .MakeGenericMethod(resultType)
                .Invoke(null, [executionResult])!;
        }
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable)
            : base(enumerable)
        {
        }

        public TestAsyncEnumerable(Expression expression)
            : base(expression)
        {
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new TestAsyncEnumerator<T>(((IEnumerable<T>)this).GetEnumerator());

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }
}
