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
/// Merges timestamped draft uploads, runs soft validation (schema, quality rules, identifier),
/// and returns submission state alongside actionable next steps for submitting organisations.
/// Route: GET /submissions/{submissionId:guid}/check
/// </summary>
public sealed class GetCheckSubmissionEndpoint(
    IUseCase<GetSubmissionStatusResult, GetSubmissionStatusRequest> getSubmissionStatusUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "CheckSubmission";

    [Function(EndpointName)]
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
