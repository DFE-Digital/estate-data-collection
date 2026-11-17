using System.Net;
using System.Text;
using System.Text.Json;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Models.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EstateDataCollection.AzureFunctions.AdminUi.Proxies;

public abstract class AdminApiUseCaseProxyBase(IHttpClientFactory httpClientFactory)
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected Task<Result<TResponse>> GetAsync<TResponse>(
        string relativeUri,
        CancellationToken cancellationToken)
    {
        return SendAsync<TResponse>(HttpMethod.Get, relativeUri, content: null, cancellationToken);
    }

    protected Task<Result<TResponse>> PostJsonAsync<TBody, TResponse>(
        string relativeUri,
        TBody body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return SendAsync<TResponse>(HttpMethod.Post, relativeUri, content, cancellationToken);
    }

    protected Task<Result<TResponse>> PatchJsonAsync<TBody, TResponse>(
        string relativeUri,
        TBody body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return SendAsync<TResponse>(HttpMethod.Patch, relativeUri, content, cancellationToken);
    }

    protected Task<Result<TResponse>> PutJsonAsync<TBody, TResponse>(
        string relativeUri,
        TBody body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return SendAsync<TResponse>(HttpMethod.Put, relativeUri, content, cancellationToken);
    }

    private async Task<Result<TResponse>> SendAsync<TResponse>(
        HttpMethod method,
        string relativeUri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(AdminApiHttpClientNames.AdminApi);
        using var request = new HttpRequestMessage(method, relativeUri)
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Error.Failure(
                "AdminApi.Unavailable",
                $"Failed to contact Admin API ({relativeUri}): {ex.Message}");
        }

        using (response)
        {
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

            ApiResponse<TResponse>? envelope = null;
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                try
                {
                    envelope = JsonSerializer.Deserialize<ApiResponse<TResponse>>(rawBody, JsonOptions);
                }
                catch (JsonException ex)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return Error.Failure(
                            "AdminApi.InvalidResponse",
                            $"Admin API returned malformed JSON: {ex.Message}");
                    }
                }
            }

            if (response.IsSuccessStatusCode && envelope is { Successful: true, Data: not null })
            {
                return Result<TResponse>.Success(envelope.Data);
            }

            return MapFailure(envelope?.Problem, response.StatusCode, rawBody);
        }
    }

    private static Error MapFailure(ProblemDetails? problem, HttpStatusCode statusCode, string rawBody)
    {
        var effectiveStatus = problem?.Status ?? (int)statusCode;
        var code = string.IsNullOrWhiteSpace(problem?.Title)
            ? $"AdminApi.Http{effectiveStatus}"
            : problem.Title;
        var description = !string.IsNullOrWhiteSpace(problem?.Detail)
            ? problem.Detail
            : (string.IsNullOrWhiteSpace(rawBody)
                ? $"Admin API returned HTTP {(int)statusCode} ({statusCode})."
                : rawBody);

        var validationErrors = ExtractValidationErrors(problem);

        return effectiveStatus switch
        {
            StatusCodes.Status422UnprocessableEntity => Error.Validation(code, description, validationErrors),
            StatusCodes.Status404NotFound => Error.NotFound(code, description),
            StatusCodes.Status409Conflict => Error.Conflict(code, description),
            StatusCodes.Status401Unauthorized => Error.Unauthorized(code, description),
            StatusCodes.Status403Forbidden => Error.Forbidden(code, description),
            _ when validationErrors.Count > 0 => Error.Validation(code, description, validationErrors),
            _ => Error.Failure(code, description)
        };
    }

    private static IReadOnlyList<ValidationError> ExtractValidationErrors(ProblemDetails? problem)
    {
        if (problem?.Extensions is null ||
            !problem.Extensions.TryGetValue("errors", out var rawErrors) ||
            rawErrors is null)
        {
            return [];
        }

        var list = new List<ValidationError>();

        if (rawErrors is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var msgEl in prop.Value.EnumerateArray())
                    {
                        if (msgEl.ValueKind == JsonValueKind.String)
                        {
                            var msg = msgEl.GetString();
                            if (!string.IsNullOrWhiteSpace(msg))
                            {
                                list.Add(new ValidationError(prop.Name, msg));
                            }
                        }
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var msg = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        list.Add(new ValidationError(prop.Name, msg));
                    }
                }
            }
        }
        else if (rawErrors is IDictionary<string, string[]> dict)
        {
            foreach (var (key, messages) in dict)
            {
                foreach (var msg in messages)
                {
                    list.Add(new ValidationError(key, msg));
                }
            }
        }

        return list;
    }
}
