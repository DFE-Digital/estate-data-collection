using AzureFunctions.Extensions.Swashbuckle;
using AzureFunctions.Extensions.Swashbuckle.Attribute;
using EstateDataCollection.AzureFunctions.Public.Options;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Serves the generated OpenAPI YAML document (default: /api/openapi/v1.yaml).
/// </summary>
[AllowAnonymous]
public sealed class GetOpenApiYamlEndpoint(
    ISwashBuckleClient? swashBuckleClient = null,
    IOptions<OpenApiDocsOptions>? options = null,
    FunctionEndpointInstrumentation? instrumentation = null
) : IFunctionEndpoint
{
    public const string EndpointName = "GetOpenApiYaml";

    private readonly FunctionEndpointInstrumentation _instrumentation =
        instrumentation ?? new FunctionEndpointInstrumentation(NullLogger<FunctionEndpointInstrumentation>.Instance);

    [SwaggerIgnore]
    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.OpenApiYaml)]
        HttpRequest http,
        string documentName)
    {
        var docName = string.IsNullOrWhiteSpace(documentName) ? "v1" : documentName;
        _instrumentation.EndpointInvoked(EndpointName, docName);

        var enabled = options?.Value.EnableSpecEndpoints ?? true;
        if (!enabled || swashBuckleClient is null)
        {
            _instrumentation.EndpointCompleted(EndpointName, StatusCodes.Status404NotFound);
            return new NotFoundResult();
        }

        var result = await swashBuckleClient.CreateSwaggerYamlDocumentResult(http, docName);
        _instrumentation.EndpointCompleted(EndpointName, StatusCodes.Status200OK);
        return result;
    }
}
