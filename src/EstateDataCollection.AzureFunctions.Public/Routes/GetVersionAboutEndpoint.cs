using EstateDataCollection.AzureFunctions.Public.OpenApi;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Health and build metadata endpoint.
/// Route: GET /api/about
/// </summary>
[AllowAnonymous]
[OpenApiOrder(90)]
[OpenApiWiki("Public-API-Health", "Public API health check and version diagnostics guide")]
public sealed class GetVersionAboutEndpoint(
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "VersionAbout";

    /// <summary>
    /// Diagnostic build and service health payload.
    /// </summary>
    /// <param name="Service">Logical service name.</param>
    /// <param name="Version">Semantic/CalVer assembly version.</param>
    /// <param name="InformativeVersion">Full informational version including build metadata.</param>
    /// <param name="Sha">Git commit SHA associated with this deployment.</param>
    /// <param name="Environment">Active Azure Functions hosting environment name.</param>
    /// <param name="ServerUtc">Current server UTC timestamp.</param>
    public sealed record VersionAboutPayload(
        string Service,
        string Version,
        string InformativeVersion,
        string Sha,
        string Environment,
        DateTimeOffset ServerUtc);

    /// <summary>
    /// Get service health and build version metadata.
    /// </summary>
    /// <remarks>
    /// Anonymous diagnostic probe returning the running application version, commit SHA, hosting environment, and server UTC time.
    /// Suitable for readiness checks without requiring a FaUAPI subscription key.
    /// </remarks>
    /// <param name="req">Incoming HTTP request.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Service health and build metadata wrapped in an `ApiResponse`.</response>
    [Function(EndpointName)]
    [ProducesResponseType(typeof(ApiResponse<VersionAboutPayload>), StatusCodes.Status200OK)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = AppRoutes.PublicApi.Templates.About)]
        HttpRequest req,
        CancellationToken cancellationToken = default)
    {
        _ = req;

        return ExecuteAsync<VersionAboutPayload>(
            EndpointName,
            _ =>
            {
                var environment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "UNDECLARED";
                var payload = new VersionAboutPayload(
                    Service: nameof(EstateDataCollection),
                    Version: AppVersionProvider.Version,
                    InformativeVersion: AppVersionProvider.InformativeVersion,
                    Sha: AppVersionProvider.Sha,
                    Environment: environment,
                    ServerUtc: DateTimeOffset.UtcNow);
                return Task.FromResult(Result<VersionAboutPayload>.Success(payload));
            },
            StatusCodes.Status200OK,
            cancellationToken: cancellationToken);
    }
}
