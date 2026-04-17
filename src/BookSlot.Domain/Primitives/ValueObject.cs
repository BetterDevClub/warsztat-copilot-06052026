namespace BookSlot.Domain.Primitives;

/// <summary>
/// Base class for value objects. Equality is based on component values returned
/// by <see cref="GetEqualityComponents"/>.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>Components that participate in equality.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <inheritdoc />
    public bool Equals(ValueObject? other)
    {
        if (other is null)
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

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValueObject vo && Equals(vo);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
}
