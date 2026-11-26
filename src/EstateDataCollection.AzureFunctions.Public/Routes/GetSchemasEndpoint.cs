using AzureFunctions.Extensions.Swashbuckle.Attribute;
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
/// Lists active schemas by default, with optional query filtering for upcoming and/or retired versions.
/// Never exposes unscheduled (Draft / Validated) schemas.
/// Route: GET /api/schemas
/// </summary>
[OpenApiOrder(10)]
[OpenApiWiki("Schema-Discovery#list-schemas", "Listing active and scheduled schemas")]
public sealed class GetSchemasEndpoint(
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "GetSchemas";

    /// <summary>
    /// List publicly visible data collection schemas.
    /// </summary>
    /// <remarks>
    /// Returns active schemas by default, with optional query flags to include scheduled upcoming or retired versions
    /// and to toggle extracted field descriptors (`includeFields=true` by default). Unscheduled (`Draft` / `Validated`)
    /// schemas are never exposed on the public API.
    /// </remarks>
    /// <param name="http">Incoming HTTP request.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Catalogue of matching public schema entries wrapped in an `ApiResponse`.</response>
    [Function(EndpointName)]
    [QueryStringParameter("includeUpcoming", "Include scheduled upcoming schema versions.", DataType = typeof(bool))]
    [QueryStringParameter("includeRetired", "Include retired schema versions.", DataType = typeof(bool))]
    [QueryStringParameter("includeFields", "Include extracted field descriptors for the active version.", DataType = typeof(bool))]
    [ProducesResponseType(typeof(ApiResponse<GetSchemasResult>), StatusCodes.Status200OK)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get),
            Route = AppRoutes.PublicApi.Templates.Schemas)]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        var includeUpcoming = http.GetBoolFromQuery("includeUpcoming", defaultValue: false);
        var includeRetired = http.GetBoolFromQuery("includeRetired", defaultValue: false);
        var includeFields = http.GetBoolFromQuery("includeFields", defaultValue: true);

        return ExecuteAsync(
            EndpointName,
            getSchemasUseCase,
            () => Task.FromResult(Result<GetSchemasRequest>.Success(new GetSchemasRequest(
                SchemaName: null,
                IncludeUnscheduled: false,
                IncludeActiveVersionFields: includeFields,
                IncludeUpcoming: includeUpcoming,
                IncludeRetired: includeRetired))),
            StatusCodes.Status200OK,
            context: $"IncludeUpcoming={includeUpcoming}, IncludeRetired={includeRetired}",
            cancellationToken: cancellationToken);
    }
}
