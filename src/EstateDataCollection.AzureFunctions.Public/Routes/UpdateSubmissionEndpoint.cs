using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Updates an in-progress draft submission payload and returns any pending schema/quality validation errors.
/// Route: PUT /submissions/{submissionId:guid}
/// </summary>
public sealed class UpdateSubmissionEndpoint(
    IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest> updateUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "UpdateSubmission";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Put),
            Route = "submissions/{submissionId:guid}")]
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            EndpointName,
            updateUseCase,
            () => BindAsync(http, submissionId, cancellationToken),
            StatusCodes.Status200OK,
            context: submissionId.ToString(),
            cancellationToken: cancellationToken);

    private static async Task<Result<UpdateSubmissionRequest>> BindAsync(
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var json = await http.ReadRequestBodyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Error.Forbidden(ErrorCodes.PayloadEmpty);
        }

        return new UpdateSubmissionRequest(submissionId, json);
    }
}
