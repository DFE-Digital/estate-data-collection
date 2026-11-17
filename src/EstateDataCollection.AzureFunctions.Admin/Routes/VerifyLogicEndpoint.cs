using System.Text.Json;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes;

/// <summary>
/// Dry-run verification endpoint specifically for quality / business logic rules and fixtures.
/// Route: POST /logic/verify
/// </summary>
public sealed class VerifyLogicEndpoint(
    IUseCase<VerifySchemaResult, VerifySchemaRequest> verifyUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "VerifyLogic";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Post),
            Route = "logic/verify")]
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

        string rulesJson;
        IReadOnlyList<string> sampleValidFixtures = [];
        IReadOnlyList<string> sampleInvalidFixtures = [];

        if (rawBody.TrimStart().StartsWith('['))
        {
            rulesJson = rawBody;
        }
        else
        {
            try
            {
                var bodyDto = JsonSerializer.Deserialize<VerifySchemaBodyDto>(rawBody, DefaultJsonOptions);
                rulesJson = bodyDto?.EffectiveRulesJson ?? rawBody;
                sampleValidFixtures = bodyDto?.SampleValidFixtures ?? [];
                sampleInvalidFixtures = bodyDto?.SampleInvalidFixtures ?? [];
            }
            catch
            {
                rulesJson = rawBody;
            }
        }

        return new VerifySchemaRequest(
            SchemaJson: null,
            SampleValidFixtures: sampleValidFixtures,
            SampleInvalidFixtures: sampleInvalidFixtures,
            RulesJson: rulesJson);
    }
}
