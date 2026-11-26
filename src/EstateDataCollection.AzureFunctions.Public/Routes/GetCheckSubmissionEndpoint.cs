using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
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
/// Merges timestamped draft uploads, runs soft validation (schema, quality rules, identifier),
/// and returns submission state alongside actionable next steps for submitting organisations.
/// Route: GET /api/submissions/{submissionId:guid}/check
/// </summary>
[OpenApiOrder(70)]
[OpenApiWiki("Submissions-Lifecycle#check-submission-readiness", "Pre-finalise validation and readiness checks")]
public sealed class GetCheckSubmissionEndpoint(
    IUseCase<GetSubmissionStatusResult, GetSubmissionStatusRequest> getSubmissionStatusUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "CheckSubmission";

    /// <summary>
    /// Re-evaluate merged drafts and check readiness to finalise.
    /// </summary>
    /// <remarks>
    /// Merges all timestamped draft uploads for `{submissionId}`, runs JSON Schema, ODCS/business quality rules, and
    /// establishment identifier extraction, and returns `isReadyToFinalise`, `pendingErrors`, and `nextSteps`.
    /// </remarks>
    /// <param name="http">Incoming HTTP request.</param>
    /// <param name="submissionId">Submission session GUID returned by Create Submission.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Pre-finalise validation report and next steps wrapped in an `ApiResponse`.</response>
    /// <response code="404">No submission session exists with `{submissionId}`.</response>
    [Function(EndpointName)]
    [ProducesResponseType(typeof(ApiResponse<GetSubmissionStatusResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.CheckSubmission)]
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        _ = http;

        return ExecuteAsync(
            EndpointName,
            getSubmissionStatusUseCase,
            () => Task.FromResult(Result<GetSubmissionStatusRequest>.Success(
                new GetSubmissionStatusRequest(submissionId, ActionName: "CheckSubmission"))),
            StatusCodes.Status200OK,
            context: submissionId.ToString(),
            cancellationToken: cancellationToken);
    }
}
