using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Lists all publicly visible schemas, their versions, lifecycle states, effective dates, and extracted fields.
/// Route: GET /schemas
/// </summary>
public sealed class GetSchemasEndpoint(
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetSchemas";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.Schemas)]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        var includeUnscheduled = http.GetBoolFromQuery("includeUnscheduled", defaultValue: false);
        var includeFields = http.GetBoolFromQuery("includeFields", defaultValue: true);

        return ExecuteAsync(
            EndpointName,
            getSchemasUseCase,
            () => Task.FromResult(Result<GetSchemasRequest>.Success(new GetSchemasRequest(
                SchemaName: null,
                IncludeUnscheduled: includeUnscheduled,
                IncludeActiveVersionFields: includeFields))),
            StatusCodes.Status200OK,
            context: $"IncludeUnscheduled={includeUnscheduled}",
            cancellationToken: cancellationToken);
    }
}
