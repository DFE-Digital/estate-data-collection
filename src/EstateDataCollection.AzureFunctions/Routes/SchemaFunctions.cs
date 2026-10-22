using System.Text.Json;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Extensions;
using EstateDataCollection.AzureFunctions.Models.Dtos;
using EstateDataCollection.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.AzureFunctions.Routes;

public class SchemaFunctions(
    IUseCase<VerifySchemaResult, VerifySchemaRequest> verifyUseCase,
    IUseCase<SubmitSchemaResult, SubmitSchemaRequest> submitUseCase,
    SchemaFunctionsInstrumentation? instrumentation = null
)
{
    private readonly SchemaFunctionsInstrumentation _instrumentation =
        instrumentation ?? new SchemaFunctionsInstrumentation(NullLogger<SchemaFunctions>.Instance);

    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Dry-run verification endpoint for the Data Team / CI pipelines.
    /// Evaluates schema syntax against Draft 2020-12 and tests sample fixtures without persisting to Blob.
    /// Route: POST /schemas/verify
    /// </summary>
    [Function(nameof(VerifySchema))]
    public async Task<IActionResult> VerifySchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post),
            Route = "schemas/verify")]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        _instrumentation.VerifySchemaInvoked();

        var body = await JsonSerializer.DeserializeAsync<VerifySchemaBodyDto>(http.Body, _jsonOptions,
            cancellationToken);

        if (body is null || string.IsNullOrWhiteSpace(body.SchemaJson))
        {
            _instrumentation.EmptyRequestBodyReceived(nameof(VerifySchema));
            var err = Error.Failure(ErrorCodes.PayloadEmpty).ToActionResult();
            _instrumentation.EndpointCompleted(nameof(VerifySchema), (err as ObjectResult)?.StatusCode ?? StatusCodes.Status400BadRequest);
            return err;
        }

        var request = new VerifySchemaRequest(
            SchemaJson: body.SchemaJson,
            SampleValidFixtures: body.SampleValidFixtures ?? [],
            SampleInvalidFixtures: body.SampleInvalidFixtures ?? []
        );

        var result = await verifyUseCase.ExecuteAsync(request, cancellationToken);
        var actionResult = result.ToActionResult();
        _instrumentation.EndpointCompleted(nameof(VerifySchema), (actionResult as ObjectResult)?.StatusCode ?? StatusCodes.Status200OK);

        return actionResult;
    }

    /// <summary>
    /// Submits, schedules, and commits a validated schema contract to the "schemas" Blob container.
    /// Route: POST /schemas/{schemaName}/versions/{version}
    /// </summary>
    [Function(nameof(SubmitSchema))]
    public async Task<IActionResult> SubmitSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post),
            Route = "schemas/{schemaName}/versions/{version:int}")]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        _instrumentation.SubmitSchemaInvoked(schemaName, version);

        var body = await JsonSerializer.DeserializeAsync<SubmitSchemaBodyDto>(http.Body, _jsonOptions,
            cancellationToken);

        if (body is null || string.IsNullOrWhiteSpace(body.SchemaJson))
        {
            _instrumentation.EmptyRequestBodyReceived(nameof(SubmitSchema));
            var err = Error.Failure(ErrorCodes.PayloadEmpty).ToActionResult();
            _instrumentation.EndpointCompleted(nameof(SubmitSchema), (err as ObjectResult)?.StatusCode ?? StatusCodes.Status400BadRequest);
            return err;
        }

        var request = new SubmitSchemaRequest(
            SchemaName: schemaName,
            Version: version,
            SchemaJson: body.SchemaJson,
            EffectiveFromUtc: body.EffectiveFromUtc,
            EffectiveToUtc: body.EffectiveToUtc,
            SampleValidFixtures: body.SampleValidFixtures,
            SampleInvalidFixtures: body.SampleInvalidFixtures
        );

        var result = await submitUseCase.ExecuteAsync(request, cancellationToken);
        var actionResult = result.ToActionResult(StatusCodes.Status201Created);
        _instrumentation.EndpointCompleted(nameof(SubmitSchema), (actionResult as ObjectResult)?.StatusCode ?? StatusCodes.Status201Created);

        return actionResult;
    }
}
