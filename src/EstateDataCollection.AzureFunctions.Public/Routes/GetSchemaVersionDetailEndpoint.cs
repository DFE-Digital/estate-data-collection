using AzureFunctions.Extensions.Swashbuckle.Attribute;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
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
/// Retrieves full detail for a specific active schema version (or upcoming/retired when explicitly requested).
/// Never exposes unscheduled (Draft / Validated) schemas.
/// Route: GET /api/schemas/{schemaName}/versions/{version:int}
/// </summary>
[OpenApiOrder(30)]
[OpenApiWiki("Schema-Discovery#get-schema-version-detail", "Inspecting a specific schema version contract")]
public sealed class GetSchemaVersionDetailEndpoint(
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetSchemaVersionDetail";

    /// <summary>
    /// Get full contract detail for a specific schema version.
    /// </summary>
    /// <remarks>
    /// Returns extracted field descriptors, validation constraints, ODCS/business quality rules, establishment identifier
    /// configuration, and optionally the raw JSON Schema document (`includeRawJsonSchema=true`).
    /// </remarks>
    /// <param name="http">Incoming HTTP request.</param>
    /// <param name="schemaName">Unique schema name (e.g. `condition-survey`).</param>
    /// <param name="version">Integer version number of the schema.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Full contract metadata and field/rule definitions wrapped in an `ApiResponse`.</response>
    /// <response code="404">The specified schema version does not exist or is not publicly visible.</response>
    [Function(EndpointName)]
    [QueryStringParameter("includeUpcoming", "Allow viewing a scheduled upcoming schema version.", DataType = typeof(bool))]
    [QueryStringParameter("includeRetired", "Allow viewing a retired schema version.", DataType = typeof(bool))]
    [QueryStringParameter("includeRawJsonSchema", "Include the raw JSON Schema document string.", DataType = typeof(bool))]
    [ProducesResponseType(typeof(ApiResponse<SchemaVersionDetail>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.SchemaVersionDetail)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        var includeUpcoming = http.GetBoolFromQuery("includeUpcoming", defaultValue: false);
        var includeRetired = http.GetBoolFromQuery("includeRetired", defaultValue: false);
        var includeRawJsonSchema = http.GetBoolFromQuery("includeRawJsonSchema", defaultValue: false);

        return ExecuteAsync(
            EndpointName,
            getSchemaDetailUseCase,
            () => Task.FromResult(Result<GetSchemaDetailRequest>.Success(new GetSchemaDetailRequest(
                SchemaName: schemaName,
                Version: version,
                IncludeUnscheduled: false,
                IncludeRawJsonSchema: includeRawJsonSchema,
                IncludeUpcoming: includeUpcoming,
                IncludeRetired: includeRetired))),
            StatusCodes.Status200OK,
            context: $"{schemaName} v{version}",
            cancellationToken: cancellationToken);
    }
}
