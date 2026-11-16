using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Public.Routes;

/// <summary>
/// Retrieves a specific schema's catalogue entry including all its available versions, effective dates, and extracted fields.
/// Route: GET /schemas/{schemaName}
/// </summary>
public sealed class GetSchemaByNameEndpoint(
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetSchemaByName";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = "schemas/{schemaName}")]
        HttpRequest http,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var includeUnscheduled = http.GetBoolFromQuery("includeUnscheduled", defaultValue: false);
        var includeFields = http.GetBoolFromQuery("includeFields", defaultValue: true);

        return ExecuteAsync<GetSchemasRequest, GetSchemasResult, SchemaCatalogueEntry>(
            EndpointName,
            getSchemasUseCase,
            () => Task.FromResult(Result<GetSchemasRequest>.Success(new GetSchemasRequest(
                SchemaName: schemaName,
                IncludeUnscheduled: includeUnscheduled,
                IncludeActiveVersionFields: includeFields))),
            result =>
            {
                var entry = result.Schemas.FirstOrDefault();
                return entry is null
                    ? Error.NotFound(ErrorCodes.SchemaNotFound, new { SchemaName = schemaName, Version = "*" })
                    : Result<SchemaCatalogueEntry>.Success(entry);
            },
            StatusCodes.Status200OK,
            context: $"{schemaName} (IncludeUnscheduled={includeUnscheduled})",
            cancellationToken: cancellationToken);
    }
}
