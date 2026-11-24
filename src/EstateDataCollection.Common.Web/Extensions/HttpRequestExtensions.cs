using System.Buffers;
using System.Text;
using EstateDataCollection.Common.Web.Models.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Common.Web.Extensions;

public sealed class RequestBodyTooLargeException(long maxSizeBytes)
    : InvalidOperationException($"The request payload exceeds the configured maximum limit of {Math.Max(1, maxSizeBytes / (1024 * 1024))} MB.")
{
    public long MaxSizeBytes { get; } = maxSizeBytes;
    public int MaxSizeMb { get; } = Math.Max(1, (int)Math.Ceiling(maxSizeBytes / (1024d * 1024d)));
}

public static class HttpRequestExtensions
{
    public const string MaxBodySizeItemKey = "Security:RequestSizeLimit:MaxSizeBytes";
    private const long DefaultMaxBodySizeBytes = 15L * 1024 * 1024;

    public static int? GetIntFromQuery(this HttpRequest request, string identifier, int? defaultValue = null)
    {
        return !request.Query.TryGetValue(identifier, out var raw)
               || !int.TryParse(raw, out var result)
            ? defaultValue
            : result;
    }

    public static bool GetBoolFromQuery(this HttpRequest request, string identifier, bool defaultValue = false)
    {
        return !request.Query.TryGetValue(identifier, out var raw)
               || !bool.TryParse(raw, out var result)
            ? defaultValue
            : result;
    }

    public static async Task<string> ReadRequestBodyAsync(
        this HttpRequest request,
        CancellationToken cancellationToken)
    {
        var maxBytes = ResolveMaxBodySizeBytes(request);
        if (request.ContentLength.HasValue && request.ContentLength.Value > maxBytes)
        {
            throw new RequestBodyTooLargeException(maxBytes);
        }

        const int bufferSize = 8192;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            using var ms = new MemoryStream();
            long totalRead = 0;

            while (true)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead > maxBytes)
                {
                    throw new RequestBodyTooLargeException(maxBytes);
                }

                ms.Write(buffer, 0, read);
            }

            ms.Position = 0;
            using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static ProblemDetails CreatePayloadTooLargeProblem(long maxSizeBytes, PathString instancePath)
    {
        var maxSizeMb = Math.Max(1, (int)Math.Ceiling(maxSizeBytes / (1024d * 1024d)));
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = "Payload Too Large",
            Detail = $"The request payload exceeds the configured maximum limit of {maxSizeMb} MB.",
            Instance = instancePath
        };
        problem.Extensions["errorCode"] = "Payload.TooLarge";
        problem.Extensions["maxSizeMb"] = maxSizeMb;
        return problem;
    }

    private static long ResolveMaxBodySizeBytes(HttpRequest request)
    {
        var httpContext = request.HttpContext;
        if (httpContext?.Items.TryGetValue(MaxBodySizeItemKey, out var rawMax) == true &&
            rawMax is long configuredMax &&
            configuredMax > 0)
        {
            return configuredMax;
        }

        var options = httpContext?.RequestServices?.GetService<IOptions<RequestSizeLimitOptions>>();
        return options?.Value.MaxSizeBytes is > 0 ? options.Value.MaxSizeBytes : DefaultMaxBodySizeBytes;
    }
}
