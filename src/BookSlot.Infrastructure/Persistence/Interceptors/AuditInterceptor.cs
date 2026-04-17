using BookSlot.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookSlot.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Populates <see cref="IAuditable"/> fields on <c>Added</c> and <c>Modified</c> entities before
/// every <c>SaveChangesAsync</c>. Reads the current actor from <see cref="ICurrentUser"/> and
/// the current timestamp from <see cref="TimeProvider"/>; both are scoped/DI-managed.
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new interceptor.</summary>
    public AuditInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var context = eventData.Context;
        if (context is not null)
        {
            ApplyAudit(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var context = eventData.Context;
        if (context is not null)
        {
            ApplyAudit(context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void ApplyAudit(DbContext context)
    {
        var now = _timeProvider.GetUtcNow();
        var actor = _currentUser.IsAuthenticated
            ? _currentUser.UserId?.ToString() ?? _currentUser.Email
            : null;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    SetValue(entry, nameof(IAuditable.CreatedAt), now);
                    SetValue(entry, nameof(IAuditable.CreatedBy), actor);
                    break;
                case EntityState.Modified:
                    SetValue(entry, nameof(IAuditable.ModifiedAt), now);
                    SetValue(entry, nameof(IAuditable.ModifiedBy), actor);
                    // Guard against callers mutating immutable audit fields.
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;
            }
        }
    }

    private static void SetValue(EntityEntry<IAuditable> entry, string propertyName, object? value)
    {
        var property = entry.Metadata.FindProperty(propertyName);
        if (property is null)
        {
            // Not mapped — skip silently. Keeps the interceptor robust against
            // partially-mapped audit types during staged migrations.
            return;
        }

        entry.Property(propertyName).CurrentValue = value;
    }
}
