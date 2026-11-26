using AzureFunctions.Extensions.Swashbuckle.Attribute;
using EstateDataCollection.AzureFunctions.Public.Options;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scalar.Azure.Functions;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Serves the interactive Scalar API Reference UI at `/api/scalar/` in non-Production environments.
/// </summary>
[AllowAnonymous]
public sealed class GetScalarDocsEndpoint(
    IScalarApiReference? scalar = null,
    IOptions<OpenApiDocsOptions>? options = null,
    IHostEnvironment? hostEnvironment = null,
    FunctionEndpointInstrumentation? instrumentation = null
) : IFunctionEndpoint
{
    public const string EndpointName = "GetScalarDocs";

    private readonly FunctionEndpointInstrumentation _instrumentation =
        instrumentation ?? new FunctionEndpointInstrumentation(NullLogger<FunctionEndpointInstrumentation>.Instance);

    [SwaggerIgnore]
    [Function(EndpointName)]
    public async Task HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.ScalarDocs)]
        HttpRequest http,
        string? path)
    {
        _instrumentation.EndpointInvoked(EndpointName, path ?? "index");

        var isEnabled = options?.Value.EnableScalarUi ?? !(hostEnvironment?.IsProduction() ?? false);
        if (!isEnabled || scalar is null)
        {
            http.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            _instrumentation.EndpointCompleted(EndpointName, StatusCodes.Status404NotFound);
            return;
        }

        await scalar.HandleAsync(http.HttpContext);
        _instrumentation.EndpointCompleted(EndpointName, http.HttpContext.Response.StatusCode);
    }
}
