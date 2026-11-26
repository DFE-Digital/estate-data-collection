using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Api;

/// <summary>
/// Schedules activation windows, lifecycle transitions (Activate, Deprecate, Retire), or URN identifier configuration.
/// Route: PATCH /api/schemas/{schemaName}/versions/{version:int}/schedule
/// </summary>
[RequireRoles(AdminRoleKeys.Automation)]
public sealed class PatchScheduleSchemaEndpoint(
    IUseCase<ScheduleSchemaResult, ScheduleSchemaRequest> scheduleUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "ScheduleSchema";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Patch),
            Route = AppRoutes.AdminApi.Templates.ScheduleSchema)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            scheduleUseCase,
            () => BindAsync(http, schemaName, version, cancellationToken),
            StatusCodes.Status200OK,
            context: $"{schemaName} v{version}",
            cancellationToken: cancellationToken);
    }

    private static async Task<Result<ScheduleSchemaRequest>> BindAsync(
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        var rawBody = await http.ReadRequestBodyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return Error.Failure(ErrorCodes.PayloadEmpty);
        }

        var dtoResult = DeserialiseJson<ScheduleSchemaBodyDto>(rawBody, "Invalid schedule request JSON body.");
        if (dtoResult.IsFailure)
        {
            return dtoResult.Error;
        }

        var body = dtoResult.Value;
        return new ScheduleSchemaRequest(
            SchemaName: schemaName,
            Version: version,
            EffectiveFromUtc: body.EffectiveFromUtc,
            EffectiveToUtc: body.EffectiveToUtc,
            LifecycleAction: body.LifecycleAction,
            IdentifierConfig: body.IdentifierConfig);
    }
}
