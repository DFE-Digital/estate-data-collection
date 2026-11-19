using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Validates and finalises a draft submission, promoting it to the processed container.
/// Route: POST /submissions/{submissionId:guid}/finalise
/// </summary>
public sealed class PostFinaliseSubmissionEndpoint(
    IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest> finaliseUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "FinaliseSubmission";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post),
            Route = "submissions/{submissionId:guid}/finalise")]
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
