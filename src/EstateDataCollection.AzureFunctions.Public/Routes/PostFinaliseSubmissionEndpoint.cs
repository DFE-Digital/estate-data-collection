using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.AzureFunctions.Public.OpenApi;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Models.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Validates and finalises a draft submission, promoting it to the processed container.
/// Route: POST /api/submissions/{submissionId:guid}/finalise
/// </summary>
[OpenApiOrder(80)]
[OpenApiWiki("Submissions-Lifecycle#finalise-submission", "Strict validation and finalising a submission")]
public sealed class PostFinaliseSubmissionEndpoint(
    IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest> finaliseUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "FinaliseSubmission";

    /// <summary>
    /// Validate and finalise a draft submission.
    /// </summary>
    /// <remarks>
    /// Merges all uploaded drafts and performs strict validation against the pinned JSON Schema, ODCS quality rules,
    /// and required establishment identifier configuration. When valid, promotes the merged submission artefact
    /// to the immutable processed container and transitions the session to `Finalised`.
    /// </remarks>
    /// <param name="http">Incoming HTTP request.</param>
    /// <param name="submissionId">Submission session GUID returned by Create Submission.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Submission passed strict validation and was promoted to the processed container.</response>
    /// <response code="404">No submission session exists with `{submissionId}`.</response>
    /// <response code="409">Submission has already been finalised.</response>
    /// <response code="422">Merged payload failed JSON Schema, quality rule, or required identifier validation.</response>
    [Function(EndpointName)]
    [ProducesResponseType(typeof(ApiResponse<FinaliseSubmissionResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post),
            Route = AppRoutes.PublicApi.Templates.FinaliseSubmission)]
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        _ = http;

        return ExecuteAsync(
            EndpointName,
            finaliseUseCase,
            () => Task.FromResult(Result<FinaliseSubmissionRequest>.Success(new FinaliseSubmissionRequest(submissionId))),
            StatusCodes.Status200OK,
            context: submissionId.ToString(),
            cancellationToken: cancellationToken);
    }
}
