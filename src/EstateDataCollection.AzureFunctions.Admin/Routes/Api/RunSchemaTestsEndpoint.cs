using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Api;

/// <summary>
/// Executes stored schema regression tests (JSON Schema, business logic rules, identifier alignment,
/// and stored valid/invalid payload fixtures) across a single version or all active/non-retired schemas.
/// Route: POST /api/schemas/tests/run
/// </summary>
public sealed class RunSchemaTestsEndpoint(
    IUseCase<RunSchemaTestsResult, RunSchemaTestsRequest> runTestsUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "RunSchemaTests";

    private sealed record RunSchemaTestsBodyDto(
        string? SchemaName = null,
        int? Version = null,
        bool IncludeRetired = false);

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post), Route = "api/schemas/tests/run")]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            runTestsUseCase,
            () => BindAsync(http, cancellationToken),
            StatusCodes.Status200OK,
            context: "schema-tests",
            cancellationToken: cancellationToken);
    }

    private static async Task<Result<RunSchemaTestsRequest>> BindAsync(
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        var querySchemaName = http.Query["schemaName"].ToString().Trim();
        var queryIncludeRetired = http.GetBoolFromQuery("includeRetired", defaultValue: false);
        int? queryVersion = int.TryParse(http.Query["version"].ToString(), out var parsedVer) && parsedVer > 0
            ? parsedVer
            : null;

        var rawBody = await http.ReadRequestBodyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return new RunSchemaTestsRequest(
                SchemaName: string.IsNullOrWhiteSpace(querySchemaName) ? null : querySchemaName,
                Version: queryVersion,
                IncludeRetired: queryIncludeRetired);
        }

        var dtoResult = DeserializeJson<RunSchemaTestsBodyDto>(rawBody, "Invalid run schema tests request JSON body.");
        if (dtoResult.IsFailure)
        {
            return dtoResult.Error;
        }

        var body = dtoResult.Value;
        return new RunSchemaTestsRequest(
            SchemaName: !string.IsNullOrWhiteSpace(body.SchemaName)
                ? body.SchemaName.Trim()
                : (string.IsNullOrWhiteSpace(querySchemaName) ? null : querySchemaName),
            Version: body.Version ?? queryVersion,
            IncludeRetired: body.IncludeRetired || queryIncludeRetired);
    }
}
