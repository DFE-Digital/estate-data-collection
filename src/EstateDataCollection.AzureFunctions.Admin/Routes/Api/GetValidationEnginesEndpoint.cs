using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetValidationEngines;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Api;

/// <summary>
/// Introspection endpoint returning all registered structural, quality rule, and identifier validation engines.
/// Route: GET /api/schemas/engines
/// </summary>
[RequireRoles(AdminRoleKeys.Automation)]
public sealed class GetValidationEnginesEndpoint(
    IUseCase<GetValidationEnginesResult, GetValidationEnginesRequest> getValidationEnginesUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetValidationEngines";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = AppRoutes.AdminApi.Templates.ValidationEngines)]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            getValidationEnginesUseCase,
            () => Task.FromResult(Result<GetValidationEnginesRequest>.Success(new GetValidationEnginesRequest())),
            StatusCodes.Status200OK,
            cancellationToken: cancellationToken);
    }
}
