using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes;

/// <summary>
/// Admin view of all schemas in the registry, including unscheduled (Validated / Draft) and Retired versions by default.
/// Route: GET /api/schemas
/// </summary>
public sealed class GetAdminSchemasEndpoint(
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetAdminSchemas";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = "api/schemas")]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            getSchemasUseCase,
            () => Task.FromResult(Bind(http)),
            StatusCodes.Status200OK,
            cancellationToken: cancellationToken);
    }

    private static Result<GetSchemasRequest> Bind(HttpRequest http)
    {
        var schemaName = http.Query.TryGetValue("schemaName", out var sName) && !string.IsNullOrWhiteSpace(sName)
            ? sName.ToString()
            : null;
        var includeUnscheduled = http.GetBoolFromQuery("includeUnscheduled", defaultValue: true);
        var includeFields = http.GetBoolFromQuery("includeFields", defaultValue: true);

        return new GetSchemasRequest(
            SchemaName: schemaName,
            IncludeUnscheduled: includeUnscheduled,
            IncludeActiveVersionFields: includeFields);
    }
}
