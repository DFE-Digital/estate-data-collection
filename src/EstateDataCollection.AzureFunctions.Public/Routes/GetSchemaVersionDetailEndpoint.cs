using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Retrieves full detail for a specific schema version, including its lifecycle state, effective dates,
/// recursively extracted fields and constraints, quality rule summaries, and optional raw JSON Schema.
/// Route: GET /schemas/{schemaName}/versions/{version:int}
/// </summary>
public sealed class GetSchemaVersionDetailEndpoint(
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetSchemaVersionDetail";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.SchemaVersionDetail)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        var includeUnscheduled = http.GetBoolFromQuery("includeUnscheduled", defaultValue: false);
        var includeRawJsonSchema = http.GetBoolFromQuery("includeRawJsonSchema", defaultValue: false);

        return ExecuteAsync(
            EndpointName,
            getSchemaDetailUseCase,
            () => Task.FromResult(Result<GetSchemaDetailRequest>.Success(new GetSchemaDetailRequest(
                SchemaName: schemaName,
                Version: version,
                IncludeUnscheduled: includeUnscheduled,
                IncludeRawJsonSchema: includeRawJsonSchema))),
            StatusCodes.Status200OK,
            context: $"{schemaName} v{version}",
            cancellationToken: cancellationToken);
    }
}
