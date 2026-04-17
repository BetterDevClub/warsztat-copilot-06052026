namespace BookSlot.Domain.Abstractions;

/// <summary>
/// Marks an entity as subject to audit bookkeeping (created/modified timestamps + actor).
/// Values are populated by the <c>AuditInterceptor</c> on every <c>SaveChangesAsync</c>; the
/// domain model exposes read-only getters so business code cannot tamper with audit fields.
/// EF configures the backing fields via convention (private setters).
/// </summary>
public interface IAuditable
{
    /// <summary>UTC timestamp of initial creation.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>Identifier of the caller that created the entity, or <c>null</c> for system writes.</summary>
    string? CreatedBy { get; }

    /// <summary>UTC timestamp of the most recent modification, or <c>null</c> if never modified.</summary>
    DateTimeOffset? ModifiedAt { get; }

    /// <summary>Identifier of the caller of the last modification, or <c>null</c>.</summary>
    string? ModifiedBy { get; }
}
