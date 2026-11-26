using AzureFunctions.Extensions.Swashbuckle.Attribute;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
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
/// Retrieves a specific schema's catalogue entry (active versions by default, with optional upcoming/retired filters).
/// Never exposes unscheduled (Draft / Validated) schemas.
/// Route: GET /api/schemas/{schemaName}
/// </summary>
[OpenApiOrder(20)]
[OpenApiWiki("Schema-Discovery#get-schema-by-name", "Retrieving a named schema catalogue entry")]
public sealed class GetSchemaByNameEndpoint(
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetSchemaByName";

    /// <summary>
    /// Get catalogue details for a named schema.
    /// </summary>
    /// <remarks>
    /// Returns active versions by default for `{schemaName}`, or upcoming/retired versions when query filters
    /// are enabled. Unscheduled (`Draft` / `Validated`) schemas are never exposed on the public API.
    /// </remarks>
    /// <param name="http">Incoming HTTP request.</param>
    /// <param name="schemaName">Unique schema name (e.g. `condition-survey`).</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Catalogue entry for the requested schema wrapped in an `ApiResponse`.</response>
    /// <response code="404">No matching publicly visible schema exists for `{schemaName}`.</response>
    [Function(EndpointName)]
    [QueryStringParameter("includeUpcoming", "Include scheduled upcoming schema versions.", DataType = typeof(bool))]
    [QueryStringParameter("includeRetired", "Include retired schema versions.", DataType = typeof(bool))]
    [QueryStringParameter("includeFields", "Include extracted field descriptors for the active version.", DataType = typeof(bool))]
    [ProducesResponseType(typeof(ApiResponse<SchemaCatalogueEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.SchemaByName)]
        HttpRequest http,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var includeUpcoming = http.GetBoolFromQuery("includeUpcoming", defaultValue: false);
        var includeRetired = http.GetBoolFromQuery("includeRetired", defaultValue: false);
        var includeFields = http.GetBoolFromQuery("includeFields", defaultValue: true);

        return ExecuteAsync<GetSchemasRequest, GetSchemasResult, SchemaCatalogueEntry>(
            EndpointName,
            getSchemasUseCase,
            () => Task.FromResult(Result<GetSchemasRequest>.Success(new GetSchemasRequest(
                SchemaName: schemaName,
                IncludeUnscheduled: false,
                IncludeActiveVersionFields: includeFields,
                IncludeUpcoming: includeUpcoming,
                IncludeRetired: includeRetired))),
            result =>
            {
                var entry = result.Schemas.FirstOrDefault();
                return entry is null
                    ? Error.NotFound(ErrorCodes.SchemaNotFound, new { SchemaName = schemaName, Version = "*" })
                    : Result<SchemaCatalogueEntry>.Success(entry);
            },
            StatusCodes.Status200OK,
            context: $"{schemaName} (IncludeUpcoming={includeUpcoming}, IncludeRetired={includeRetired})",
            cancellationToken: cancellationToken);
    }
}
