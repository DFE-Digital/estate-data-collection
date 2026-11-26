using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace EstateDataCollection.Common.Web.Models.Results;

/// <summary>
/// Standard response envelope returned by all Estate Data Collection HTTP endpoints.
/// </summary>
/// <typeparam name="T">Payload type returned when <see cref="Successful"/> is <see langword="true"/>.</typeparam>
public sealed record ApiResponse<T>
{
    /// <summary>
    /// Indicates whether the request completed successfully.
    /// </summary>
    public bool Successful { get; init; }

    /// <summary>
    /// Response payload populated when <see cref="Successful"/> is <see langword="true"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; init; }

    /// <summary>
    /// RFC 9457 problem details populated when <see cref="Successful"/> is <see langword="false"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProblemDetails? Problem { get; init; }

    public static ApiResponse<T> Success(T value)
    {
        return new ApiResponse<T>
        {
            Successful = true,
            Data = value,
            Problem = null
        };
    }

    public static ApiResponse<T> Failure(ProblemDetails problem)
    {
        return new ApiResponse<T>
        {
            Successful = false,
            Data = default,
            Problem = problem
        };
    }
}
