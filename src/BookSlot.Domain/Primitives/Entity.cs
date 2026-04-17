namespace BookSlot.Domain.Primitives;

/// <summary>Base class for entities with a strongly-typed identity.</summary>
/// <typeparam name="TId">The identity type (typically <see cref="Guid"/> or a strongly-typed id).</typeparam>
public abstract class Entity<TId>
    where TId : notnull
{
    /// <summary>Entity identity.</summary>
    public TId Id { get; protected set; } = default!;

    /// <summary>Parameterless constructor required by EF Core.</summary>
    protected Entity() { }

    /// <summary>Creates an entity with the given identity.</summary>
    protected Entity(TId id)
    {
        Id = id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);

    /// <summary>Reference equality check.</summary>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    /// <summary>Reference inequality check.</summary>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
