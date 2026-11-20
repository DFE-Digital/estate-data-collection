using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.AzureFunctions.Admin.Helpers;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Api;

/// <summary>
/// Persists a verified schema version (JSON Schema + Quality Rules or ODCS YAML) as Validated (unscheduled).
/// Route: POST /api/schemas/{schemaName}/versions/{version:int}
/// </summary>
public sealed class PostSubmitSchemaEndpoint(
    IUseCase<SubmitSchemaResult, SubmitSchemaRequest> submitUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "SubmitSchema";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post),
            Route = AppRoutes.AdminApi.Templates.SubmitSchema)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            submitUseCase,
            () => BindAsync(http, schemaName, version, cancellationToken),
            StatusCodes.Status201Created,
            context: $"{schemaName} v{version}",
            cancellationToken: cancellationToken);
    }

    private static async Task<Result<SubmitSchemaRequest>> BindAsync(
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

        if (AdminRequestHelpers.IsYamlRequest(http, rawBody))
        {
            return new SubmitSchemaRequest(
                SchemaName: schemaName,
                Version: version,
                SchemaJson: string.Empty,
                OdcsYaml: rawBody,
                FileName: AdminRequestHelpers.GetFileNameHint(http),
                IdentifierConfig: AdminRequestHelpers.GetIdentifierConfigHint(http));
        }

        var dtoResult = DeserializeJson<SubmitSchemaBodyDto>(rawBody, "Invalid submit schema request JSON body.");
        if (dtoResult.IsFailure)
        {
            return dtoResult.Error;
        }

        var body = dtoResult.Value;
        if (string.IsNullOrWhiteSpace(body.SchemaJson) && string.IsNullOrWhiteSpace(body.OdcsYaml))
        {
            return Error.Failure(ErrorCodes.PayloadEmpty);
        }

        return new SubmitSchemaRequest(
            SchemaName: schemaName,
            Version: version,
            SchemaJson: body.SchemaJson ?? string.Empty,
            SampleValidFixtures: body.SampleValidFixtures,
            SampleInvalidFixtures: body.SampleInvalidFixtures,
            RulesJson: body.EffectiveRulesJson,
            OdcsYaml: body.OdcsYaml,
            FileName: body.FileName ?? AdminRequestHelpers.GetFileNameHint(http),
            IdentifierConfig: body.IdentifierConfig ?? AdminRequestHelpers.GetIdentifierConfigHint(http));
    }
}
