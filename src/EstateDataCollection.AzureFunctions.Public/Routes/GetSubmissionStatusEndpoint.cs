using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Returns the current lifecycle state and pre-finalise validation readiness for a submission session.
/// Route: GET /submissions/{submissionId:guid}
/// </summary>
public sealed class GetSubmissionStatusEndpoint(
    IUseCase<GetSubmissionStatusResult, GetSubmissionStatusRequest> getSubmissionStatusUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetSubmissionStatus";

    [Function(EndpointName)]
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
