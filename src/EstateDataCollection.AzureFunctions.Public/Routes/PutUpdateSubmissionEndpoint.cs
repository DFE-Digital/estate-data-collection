using AzureFunctions.Extensions.Swashbuckle.Attribute;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
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
/// Updates an in-progress draft submission payload and returns any pending schema/quality validation errors.
/// Route: PUT /api/submissions/{submissionId:guid}
/// </summary>
[OpenApiOrder(50)]
[OpenApiWiki("Submissions-Lifecycle#upload-draft-payload", "Uploading and merging incremental draft payloads")]
public sealed class PutUpdateSubmissionEndpoint(
    IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest> updateUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "UpdateSubmission";

    /// <summary>
    /// Upload or merge a draft JSON survey payload.
    /// </summary>
    /// <remarks>
    /// Stores a timestamped draft upload (full or partial JSON object) for `{submissionId}`, merges all uploaded
    /// drafts chronologically, and returns soft validation feedback (`isSuccessful` and `pendingErrors`) without
    /// rejecting incomplete intermediate saves.
    /// </remarks>
    /// <param name="http">Incoming HTTP request containing the JSON survey payload in the request body.</param>
    /// <param name="submissionId">Submission session GUID returned by Create Submission.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Draft payload merged; review `isSuccessful` and `pendingErrors` for remaining validation items.</response>
    /// <response code="400">Request body was empty or not valid JSON.</response>
    /// <response code="404">No submission session exists with `{submissionId}`.</response>
    /// <response code="409">Submission has already been finalised and cannot accept further draft updates.</response>
    [Function(EndpointName)]
    [ProducesResponseType(typeof(ApiResponse<UpdateSubmissionResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Put),
            Route = AppRoutes.PublicApi.Templates.UpdateSubmission)]
        [RequestBodyType(typeof(object), "Dynamic JSON survey payload conforming to the pinned schema version.")]
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            updateUseCase,
            () => BindAsync(http, submissionId, cancellationToken),
            StatusCodes.Status200OK,
            context: submissionId.ToString(),
            cancellationToken: cancellationToken);
    }

    private static async Task<Result<UpdateSubmissionRequest>> BindAsync(
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var json = await http.ReadRequestBodyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Error.Failure(ErrorCodes.PayloadEmpty);
        }

        return new UpdateSubmissionRequest(submissionId, json);
    }
}
