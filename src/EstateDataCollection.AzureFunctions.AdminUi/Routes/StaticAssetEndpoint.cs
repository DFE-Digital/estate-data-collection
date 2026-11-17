using EstateDataCollection.Admin.UI.Assets;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.AzureFunctions.AdminUi.Routes;

public sealed class StaticAssetEndpoint(
    FunctionEndpointInstrumentation? instrumentation = null
) : IFunctionEndpoint
{
    public const string EndpointName = "AdminUiStaticAsset";

    private readonly FunctionEndpointInstrumentation _instrumentation =
        instrumentation ?? new FunctionEndpointInstrumentation(NullLogger<FunctionEndpointInstrumentation>.Instance);

    [Function(EndpointName)]
    public IActionResult Handle(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get), Route = "assets/{*assetPath}")]
        HttpRequest http,
        string assetPath)
    {
        _instrumentation.EndpointInvoked(EndpointName, assetPath);

        if (!EmbeddedUiAssetProvider.TryReadAsset(assetPath, out var bytes, out var contentType))
        {
            _instrumentation.EndpointCompleted(EndpointName, StatusCodes.Status404NotFound);
            return new NotFoundResult();
        }

        http.HttpContext?.Response.Headers.TryAdd("Cache-Control", "public, max-age=86400");
        _instrumentation.EndpointCompleted(EndpointName, StatusCodes.Status200OK);
        return new FileContentResult(bytes, contentType);
    }
}
