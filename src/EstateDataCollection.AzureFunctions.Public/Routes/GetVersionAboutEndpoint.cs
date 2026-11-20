using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Health and build metadata endpoint.
/// Route: GET /about
/// </summary>
public sealed class GetVersionAboutEndpoint(
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "VersionAbout";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = AppRoutes.PublicApi.Templates.About)]
        HttpRequest req,
        CancellationToken cancellationToken = default)
    {
        _ = req;

        return ExecuteAsync<object>(
            EndpointName,
            _ =>
            {
                var environment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "UNDECLARED";
                object payload = new
                {
                    service = nameof(EstateDataCollection),
                    version = AppVersionProvider.Version,
                    informativeVersion = AppVersionProvider.InformativeVersion,
                    sha = AppVersionProvider.Sha,
                    environment,
                    serverUtc = DateTimeOffset.UtcNow
                };
                return Task.FromResult(Result<object>.Success(payload));
            },
            StatusCodes.Status200OK,
            cancellationToken: cancellationToken);
    }
}
