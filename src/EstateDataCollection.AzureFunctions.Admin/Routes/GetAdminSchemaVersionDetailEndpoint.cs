using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes;

/// <summary>
/// Admin view of a specific schema version's metadata, fields, rules, and raw JSON schema.
/// Route: GET /schemas/{schemaName}/versions/{version:int}
/// </summary>
public sealed class GetAdminSchemaVersionDetailEndpoint(
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetAdminSchemaVersionDetail";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = "schemas/{schemaName}/versions/{version:int}")]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            getSchemaDetailUseCase,
            () => Task.FromResult(Bind(http, schemaName, version)),
            StatusCodes.Status200OK,
            context: $"{schemaName} v{version}",
            cancellationToken: cancellationToken);
    }

    private static Result<GetSchemaDetailRequest> Bind(HttpRequest http, string schemaName, int version)
    {
        return new GetSchemaDetailRequest(
            SchemaName: schemaName,
            Version: version,
            IncludeUnscheduled: http.GetBoolFromQuery("includeUnscheduled", defaultValue: true),
            IncludeRawJsonSchema: http.GetBoolFromQuery("includeRawJsonSchema", defaultValue: true));
    }
}
