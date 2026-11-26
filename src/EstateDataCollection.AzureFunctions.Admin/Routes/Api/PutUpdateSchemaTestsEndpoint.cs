using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
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
/// Replaces and re-verifies the stored valid/invalid golden regression test fixtures
/// (.valid.json / .invalid.json) for an existing schema version without mutating the schema contract.
/// Route: PUT /api/schemas/{schemaName}/versions/{version:int}/tests
/// </summary>
[RequireRoles(AdminRoleKeys.Automation)]
public sealed class PutUpdateSchemaTestsEndpoint(
    IUseCase<UpdateSchemaTestsResult, UpdateSchemaTestsRequest> updateTestsUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "UpdateSchemaTests";

    private sealed record UpdateSchemaTestsBodyDto(
        IReadOnlyList<string>? ValidFixtures = null,
        IReadOnlyList<string>? InvalidFixtures = null);

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Put),
            Route = AppRoutes.AdminApi.Templates.UpdateSchemaTests)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            updateTestsUseCase,
            () => BindAsync(http, schemaName, version, cancellationToken),
            StatusCodes.Status200OK,
            context: $"{schemaName} v{version}",
            cancellationToken: cancellationToken);
    }

    private static async Task<Result<UpdateSchemaTestsRequest>> BindAsync(
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

        var dtoResult = DeserialiseJson<UpdateSchemaTestsBodyDto>(rawBody, "Invalid update schema tests request JSON body.");
        if (dtoResult.IsFailure)
        {
            return dtoResult.Error;
        }

        var body = dtoResult.Value;
        return new UpdateSchemaTestsRequest(
            SchemaName: schemaName,
            Version: version,
            ValidFixtures: body.ValidFixtures ?? [],
            InvalidFixtures: body.InvalidFixtures ?? []);
    }
}
