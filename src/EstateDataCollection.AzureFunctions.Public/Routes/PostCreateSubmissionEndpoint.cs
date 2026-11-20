using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Creates a new draft submission session against an active schema version.
/// Route: POST /submissions/{schemeName}
/// </summary>
public sealed class PostCreateSubmissionEndpoint(
    IUseCase<CreateSubmissionResult, CreateSubmissionRequest> createUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "CreateSubmission";

    [Function(EndpointName)]
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
