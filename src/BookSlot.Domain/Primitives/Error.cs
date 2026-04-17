namespace BookSlot.Domain.Primitives;

/// <summary>Kind of an <see cref="Error"/> — drives HTTP status mapping at the API boundary.</summary>
public enum ErrorType
{
    /// <summary>No error. Use <see cref="Error.None"/>.</summary>
    None = 0,

    /// <summary>Invalid input or business-rule violation (HTTP 400).</summary>
    Validation,

    /// <summary>Requested resource not found (HTTP 404).</summary>
    NotFound,

    /// <summary>Conflict with current state, e.g. concurrency (HTTP 409).</summary>
    Conflict,

    /// <summary>Unauthenticated caller (HTTP 401).</summary>
    Unauthorized,

    /// <summary>Authenticated but not allowed (HTTP 403).</summary>
    Forbidden,

    /// <summary>Unexpected failure (HTTP 500).</summary>
    Failure,
}

/// <summary>Structured error returned by domain/application operations.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>"No error" sentinel used by successful <c>Result</c>s.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Null value received where a value was required.</summary>
    public static readonly Error NullValue = new("Error.NullValue", "A null value was provided.", ErrorType.Validation);

    /// <summary>Factory for a validation error.</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>Factory for a not-found error.</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Factory for a conflict error.</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Factory for a forbidden error.</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>Factory for an unauthorized error.</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>Factory for a generic failure.</summary>
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}
