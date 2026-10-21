using EstateDataCollection.AzureFunctions.Models.Results;
using EstateDataCollection.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EstateDataCollection.AzureFunctions.Extensions;

public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            var body = ApiResponse<T>.Success(result.Value);
            return new ObjectResult(body) { StatusCode = successStatusCode };
        }

        return CreateFailureResult<T>(result.Error);
    }

    public static IActionResult ToActionResult(this Result result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            var body = ApiResponse<object?>.Success(null);
            return new ObjectResult(body) { StatusCode = successStatusCode };
        }

        return CreateFailureResult<object?>(result.Error);
    }

    public static IActionResult ToActionResult(this Error error)
    {
        return CreateFailureResult<object?>(error);
    }

    private static IActionResult CreateFailureResult<T>(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = error.Code,
            Detail = error.Description
        };

        if (error.ValidationErrors.Count > 0)
        {
            problemDetails.Extensions["errors"] = error.ValidationErrors
                .GroupBy(e => e.PropertyPath)
                .ToDictionary(
                    g => string.IsNullOrEmpty(g.Key) ? "payload" : g.Key,
                    g => g.Select(e => e.Message).ToArray()
                );
        }

        var body = ApiResponse<T>.Failure(problemDetails);
        return new ObjectResult(body) { StatusCode = statusCode };
    }
}