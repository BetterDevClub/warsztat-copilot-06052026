namespace BookSlot.Domain.Primitives;

/// <summary>Outcome of an operation — either success or a single <see cref="Error"/>.</summary>
public class Result
{
    /// <summary>Creates a new result.</summary>
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The error, or <see cref="Error.None"/> on success.</summary>
    public Error Error { get; }

    /// <summary>Success.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Success with a value.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Failure.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Failure of a typed result.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}
