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
/// Lists active schemas by default, with optional query filtering for upcoming and/or retired versions.
/// Never exposes unscheduled (Draft / Validated) schemas.
/// Route: GET /api/schemas
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
