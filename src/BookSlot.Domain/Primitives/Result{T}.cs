namespace BookSlot.Domain.Primitives;

/// <summary>Outcome of an operation that produces a value on success.</summary>
/// <typeparam name="TValue">The value type.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    /// <summary>Creates a new typed result.</summary>
    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>The value; throws if the result is a failure.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    /// <summary>Implicit conversion from a value to a successful result.</summary>
    public static implicit operator Result<TValue>(TValue value) =>
        value is not null ? Success(value) : Failure<TValue>(Error.NullValue);
}
