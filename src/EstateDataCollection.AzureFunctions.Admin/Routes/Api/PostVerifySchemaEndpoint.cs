using System.Text.Json;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
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
/// Dry-run verification endpoint for the Data Team / CI pipelines.
/// Always returns 200 OK with a structured verification report (issues grouped by severity,
/// contract consistency analyzer findings, valid/invalid fixture outcomes, and engine traces),
/// returning 400 Bad Request only when the HTTP request body is empty or completely unreadable.
/// Route: POST /api/schemas/verify
/// </summary>
public sealed class PostVerifySchemaEndpoint(
    IUseCase<VerifySchemaResult, VerifySchemaRequest> verifyUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "VerifySchema";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post),
            Route = AppRoutes.AdminApi.Templates.VerifySchema)]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            verifyUseCase,
            () => BindAsync(http, cancellationToken),
            StatusCodes.Status200OK,
            cancellationToken: cancellationToken);
    }

    private static async Task<Result<VerifySchemaRequest>> BindAsync(HttpRequest http, CancellationToken cancellationToken)
    {
        var rawBody = await http.ReadRequestBodyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return Error.Failure(ErrorCodes.PayloadEmpty);
        }

        if (rawBody.TrimStart().StartsWith('['))
        {
            return new VerifySchemaRequest(
                SchemaJson: null,
                SampleValidFixtures: [],
                SampleInvalidFixtures: [],
                RulesJson: rawBody,
                FileName: AdminRequestHelpers.GetFileNameHint(http),
                IdentifierConfig: AdminRequestHelpers.GetIdentifierConfigHint(http));
        }

        VerifySchemaBodyDto? body = null;
        var isRawYaml = AdminRequestHelpers.IsYamlRequest(http, rawBody);

        if (!isRawYaml)
        {
            try
            {
                body = JsonSerializer.Deserialize<VerifySchemaBodyDto>(rawBody, DefaultJsonOptions);
            }
            catch
            {
                // Fallback if raw YAML was posted without YAML Content-Type header
                if (rawBody.Contains("kind: DataContract", StringComparison.OrdinalIgnoreCase) ||
                    rawBody.Contains("apiVersion:", StringComparison.OrdinalIgnoreCase))
                {
                    isRawYaml = true;
                }
            }
        }

        if (isRawYaml)
        {
            return new VerifySchemaRequest(
                SchemaJson: null,
                SampleValidFixtures: [],
                SampleInvalidFixtures: [],
                OdcsYaml: rawBody,
                FileName: AdminRequestHelpers.GetFileNameHint(http),
                IdentifierConfig: AdminRequestHelpers.GetIdentifierConfigHint(http));
        }

        if (body is null ||
            (string.IsNullOrWhiteSpace(body.SchemaJson) &&
             string.IsNullOrWhiteSpace(body.RulesJson) &&
             string.IsNullOrWhiteSpace(body.OdcsYaml)))
        {
            return Error.Failure(ErrorCodes.PayloadEmpty);
        }

        return new VerifySchemaRequest(
            SchemaJson: body.SchemaJson,
            SampleValidFixtures: body.SampleValidFixtures ?? [],
            SampleInvalidFixtures: body.SampleInvalidFixtures ?? [],
            RulesJson: body.RulesJson,
            OdcsYaml: body.OdcsYaml,
            FileName: body.FileName ?? AdminRequestHelpers.GetFileNameHint(http),
            IdentifierConfig: body.IdentifierConfig ?? AdminRequestHelpers.GetIdentifierConfigHint(http),
            ValidationPolicy: body.ValidationPolicy,
            AmbientFixtures: body.AmbientFixtures);
    }
}
