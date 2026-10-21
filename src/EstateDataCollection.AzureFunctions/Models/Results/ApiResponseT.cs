using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace EstateDataCollection.AzureFunctions.Models.Results;

public sealed record ApiResponse<T>
{
    public bool Successful { get; init; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; init; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProblemDetails? Problem { get; init; }

    public static ApiResponse<T> Success(T value) => new()
    {
        Successful = true,
        Data = value,
        Problem = null
    };

    public static ApiResponse<T> Failure(ProblemDetails problem) => new()
    {
        Successful = false,
        Data = default,
        Problem = problem
    };
}