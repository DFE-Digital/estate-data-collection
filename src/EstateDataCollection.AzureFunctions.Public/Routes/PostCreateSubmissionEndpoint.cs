using AzureFunctions.Extensions.Swashbuckle.Attribute;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.AzureFunctions.Public.OpenApi;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using EstateDataCollection.Common.Web.Models.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Creates a new draft submission session against an active schema version.
/// Route: POST /api/submissions/{schemeName}
/// </summary>
[OpenApiOrder(40)]
[OpenApiWiki("Submissions-Lifecycle#create-submission", "Creating a draft submission session")]
public sealed class PostCreateSubmissionEndpoint(
    IUseCase<CreateSubmissionResult, CreateSubmissionRequest> createUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "CreateSubmission";

    /// <summary>
    /// Create a new draft submission session.
    /// </summary>
    /// <remarks>
    /// Initialises a submission session and pins it to the currently active version of `{schemeName}` (or to an
    /// explicit `version` if provided and currently active). Subsequent draft uploads, checks, and finalisation
    /// validate against that pinned schema version even if a newer version becomes active later.
    /// </remarks>
    /// <param name="http">Incoming HTTP request.</param>
    /// <param name="schemeName">Target schema name for this submission.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="201">Draft submission session created with its generated `submissionId` and pinned schema version.</response>
    /// <response code="404">No active or matching schema exists for `{schemeName}`.</response>
    /// <response code="422">Requested schema version is not currently active or cannot accept submissions.</response>
    [Function(EndpointName)]
    [QueryStringParameter("version", "Optional explicit schema version to pin; defaults to the currently active version.", DataType = typeof(int))]
    [ProducesResponseType(typeof(ApiResponse<CreateSubmissionResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post),
            Route = AppRoutes.PublicApi.Templates.CreateSubmission)]
        HttpRequest http,
        string schemeName,
        CancellationToken cancellationToken)
    {
        var version = http.GetIntFromQuery("version");

        return ExecuteAsync(
            EndpointName,
            createUseCase,
            () => Task.FromResult(Result<CreateSubmissionRequest>.Success(new CreateSubmissionRequest(schemeName, version))),
            StatusCodes.Status201Created,
            context: $"{schemeName} (version: {version?.ToString() ?? "latest"})",
            cancellationToken: cancellationToken);
    }
}
