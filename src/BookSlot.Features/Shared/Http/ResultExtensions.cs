using BookSlot.Domain.Primitives;
using Microsoft.AspNetCore.Http;

namespace BookSlot.Features.Shared.Http;

/// <summary>
/// Converts <see cref="Result"/>/<see cref="Result{TValue}"/> to minimal-API
/// <see cref="IResult"/> responses — successes pass through, failures become ProblemDetails.
/// </summary>
public static class ResultExtensions
{
    /// <summary>Maps a non-generic <see cref="Result"/> to an HTTP response.</summary>
    public static IResult ToHttpResult(this Result result, int successStatus = StatusCodes.Status204NoContent)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? successStatus switch
            {
                StatusCodes.Status204NoContent => Results.NoContent(),
                StatusCodes.Status200OK => Results.Ok(),
                _ => Results.StatusCode(successStatus),
            }
            : ToProblem(result.Error);
    }

    /// <summary>Maps a <see cref="Result{TValue}"/> to an HTTP response.</summary>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result, int successStatus = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? successStatus switch
            {
                StatusCodes.Status200OK => Results.Ok(result.Value),
                StatusCodes.Status201Created => Results.Json(result.Value, statusCode: StatusCodes.Status201Created),
                _ => Results.Json(result.Value, statusCode: successStatus),
            }
            : ToProblem(result.Error);
    }

    /// <summary>Maps a <see cref="Result{TValue}"/> that returns a created resource.</summary>
    public static IResult ToCreatedResult<TValue>(this Result<TValue> result, string location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        return result.IsSuccess
            ? Results.Created(location, result.Value)
            : ToProblem(result.Error);
    }

    private static IResult ToProblem(Error error) => error.Type switch
    {
        ErrorType.NotFound => Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: StatusCodes.Status404NotFound),
        ErrorType.Conflict => Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: StatusCodes.Status409Conflict),
        ErrorType.Unauthorized => Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: StatusCodes.Status401Unauthorized),
        ErrorType.Forbidden => Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: StatusCodes.Status403Forbidden),
        ErrorType.Validation => Results.ValidationProblem(
            errors: new Dictionary<string, string[]> { [error.Code] = [error.Message] },
            title: "Validation failed",
            statusCode: StatusCodes.Status400BadRequest),
        _ => Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: StatusCodes.Status500InternalServerError),
    };
}
