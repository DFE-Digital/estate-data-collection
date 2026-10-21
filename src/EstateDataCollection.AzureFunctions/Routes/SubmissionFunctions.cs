using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Extensions;
using EstateDataCollection.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.AzureFunctions.Routes;

public class SubmissionFunctions(
    IUseCase<CreateSubmissionResult, CreateSubmissionRequest> createUseCase,
    IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest> updateUseCase,
    IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest> finaliseUseCase,
    SubmissionFunctionsInstrumentation? instrumentation = null
)
{
    private readonly SubmissionFunctionsInstrumentation _instrumentation =
        instrumentation ?? new SubmissionFunctionsInstrumentation(NullLogger<SubmissionFunctions>.Instance);

    [Function(nameof(CreateSubmission))]
    public async Task<IActionResult> CreateSubmission(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post),
            Route = "submissions/{schemeName}")]
        HttpRequest http,
        string schemeName,
        CancellationToken cancellationToken)
    {
        var version = http.GetIntFromQuery("version");
        _instrumentation.CreateSubmissionInvoked(schemeName, version);

        var request = new CreateSubmissionRequest(schemeName, version);
        var result = await createUseCase.ExecuteAsync(request, cancellationToken);
        var actionResult = result.ToActionResult(StatusCodes.Status201Created);
        _instrumentation.EndpointCompleted(nameof(CreateSubmission), (actionResult as ObjectResult)?.StatusCode ?? StatusCodes.Status201Created);
        
        return actionResult;
    }

    [Function(nameof(UpdateSubmission))]
    public async Task<IActionResult> UpdateSubmission(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Put),
            Route = "submissions/{submissionId:guid}")]
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        _instrumentation.UpdateSubmissionInvoked(submissionId);

        using var reader = new StreamReader(http.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);
        
        if (string.IsNullOrWhiteSpace(json))
        {
            _instrumentation.EmptyPayloadReceived(nameof(UpdateSubmission), submissionId);
            var err = Error.Forbidden(ErrorCodes.PayloadEmpty).ToActionResult();
            _instrumentation.EndpointCompleted(nameof(UpdateSubmission), (err as ObjectResult)?.StatusCode ?? StatusCodes.Status403Forbidden);
            return err;
        }
        
        var request = new UpdateSubmissionRequest(submissionId, json);
        var result = await updateUseCase.ExecuteAsync(request, cancellationToken);
        var actionResult = result.ToActionResult();
        _instrumentation.EndpointCompleted(nameof(UpdateSubmission), (actionResult as ObjectResult)?.StatusCode ?? StatusCodes.Status200OK);

        return actionResult;
    }

    [Function(nameof(FinaliseSubmission))]
    public async Task<IActionResult> FinaliseSubmission(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post),
            Route = "submissions/{submissionId:guid}/finalise")]
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        _instrumentation.FinaliseSubmissionInvoked(submissionId);

        var request = new FinaliseSubmissionRequest(submissionId);
        var result = await finaliseUseCase.ExecuteAsync(request, cancellationToken);
        var actionResult = result.ToActionResult();
        _instrumentation.EndpointCompleted(nameof(FinaliseSubmission), (actionResult as ObjectResult)?.StatusCode ?? StatusCodes.Status200OK);

        return actionResult;
    }
}
