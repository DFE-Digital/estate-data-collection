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
/// Returns the current lifecycle state and pre-finalise validation readiness for a submission session.
/// Route: GET /api/submissions/{submissionId:guid}
/// </summary>
[OpenApiOrder(60)]
[OpenApiWiki("Submissions-Lifecycle#get-submission-status", "Checking submission status")]
public sealed class GetSubmissionStatusEndpoint(
    IUseCase<GetSubmissionStatusResult, GetSubmissionStatusRequest> getSubmissionStatusUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetSubmissionStatus";

    /// <summary>
    /// Get current lifecycle status for a submission session.
    /// </summary>
    /// <remarks>
    /// Returns the submission's lifecycle state (`Draft` or `Finalised`), pinned schema version, draft chunk count,
    /// current pending validation errors, and actionable next steps.
    /// </remarks>
    /// <param name="http">Incoming HTTP request.</param>
    /// <param name="submissionId">Submission session GUID returned by Create Submission.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Submission lifecycle snapshot and validation summary wrapped in an `ApiResponse`.</response>
    /// <response code="404">No submission session exists with `{submissionId}`.</response>
    [Function(EndpointName)]
    [ProducesResponseType(typeof(ApiResponse<GetSubmissionStatusResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.SubmissionStatus)]
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        _ = http;

        return ExecuteAsync(
            EndpointName,
            getSubmissionStatusUseCase,
            () => Task.FromResult(Result<GetSubmissionStatusRequest>.Success(
                new GetSubmissionStatusRequest(submissionId))),
            StatusCodes.Status200OK,
            context: submissionId.ToString(),
            cancellationToken: cancellationToken);
    }
}
