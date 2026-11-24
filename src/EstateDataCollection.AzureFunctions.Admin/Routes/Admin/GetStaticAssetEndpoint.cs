using EstateDataCollection.Admin.UI.Assets;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class GetStaticAssetEndpoint(
    FunctionEndpointInstrumentation? instrumentation = null,
    TimeSpan? cacheDuration = null
) : IFunctionEndpoint
{
    public const string EndpointName = "AdminUiStaticAsset";

    public static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(1);

    private readonly FunctionEndpointInstrumentation _instrumentation =
        instrumentation ?? new FunctionEndpointInstrumentation(NullLogger<FunctionEndpointInstrumentation>.Instance);

    private readonly TimeSpan _cacheDuration = cacheDuration ?? DefaultCacheDuration;

    [Function(EndpointName)]
    public IActionResult Handle(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get), Route = AppRoutes.Assets.Templates.Asset)]
        HttpRequest http,
        string assetPath)
    {
        _instrumentation.EndpointInvoked(EndpointName, assetPath);

        if (!EmbeddedUiAssetProvider.TryReadAsset(assetPath, out var bytes, out var contentType, out var etag))
        {
            _instrumentation.EndpointCompleted(EndpointName, StatusCodes.Status404NotFound);
            return new NotFoundResult();
        }

        var maxAgeSeconds = Math.Max(0, (int)_cacheDuration.TotalSeconds);
        var cacheControl = $"public, max-age={maxAgeSeconds}, must-revalidate";

        var responseHeaders = http.HttpContext?.Response.Headers;
        responseHeaders?.TryAdd("Cache-Control", cacheControl);
        responseHeaders?.TryAdd("ETag", etag);

        if (IsNotModified(http, etag))
        {
            _instrumentation.EndpointCompleted(EndpointName, StatusCodes.Status304NotModified);
            return new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        _instrumentation.EndpointCompleted(EndpointName, StatusCodes.Status200OK);
        return new FileContentResult(bytes, contentType);
    }

    private static bool IsNotModified(HttpRequest http, string etag)
    {
        if (!http.Headers.TryGetValue("If-None-Match", out var ifNoneMatchValues))
        {
            return false;
        }

        foreach (var rawHeader in ifNoneMatchValues)
        {
            if (string.IsNullOrWhiteSpace(rawHeader))
            {
                continue;
            }

            foreach (var candidate in rawHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (candidate == "*" ||
                    string.Equals(candidate, etag, StringComparison.Ordinal) ||
                    string.Equals(candidate, $"W/{etag}", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
