using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using Json.Schema;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Validation;

public class JsonSchemaNetValidator(
    JsonSchemaNetValidatorInstrumentation? instrumentation = null,
    IOptions<ValidationOptions>? options = null
) : ISchemaValidator
{
    public ValidationEngineDescriptor Descriptor { get; } = new(
        EngineId: "json-schema",
        DisplayName: "JsonSchema.Net Validator",
        Kind: ValidationEngineKinds.StructuralSchema,
        Description: $"Validates payload structure against compiled JSON Schema (Draft 2020-12 / Draft-07) with closed-schema enforcement (format validation={(options?.Value.RequireFormatValidation ?? true)}).");

    private readonly JsonSchemaNetValidatorInstrumentation _instrumentation =
        instrumentation ?? new JsonSchemaNetValidatorInstrumentation(NullLogger<JsonSchemaNetValidator>.Instance);

    private readonly EvaluationOptions _evaluationOpts = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = options?.Value.RequireFormatValidation ?? true
    };

    public async Task<Result<SchemaValidationResult>> ValidateAsync(string payload, JsonSchema schema,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Error.Validation(ErrorCodes.JsonEmptyPayload);
        }

        try
        {
            _instrumentation.ValidationStarted();
            var node = JsonNode.Parse(payload);

            if (node is null)
            {
                return Error.Validation(ErrorCodes.JsonEmptyPayload);
            }
            
            var result = schema.Evaluate(node, _evaluationOpts);

            if (result.IsValid)
            {
                _instrumentation.ValidationPassed();
                return Result<SchemaValidationResult>.Success(new SchemaValidationResult());
            }

            var validationErrors = result.Details
                .Where(d => d.HasErrors)
                .SelectMany(d => d.Errors?
                    .Select(e => new ValidationError(
                        PropertyPath: d.InstanceLocation.ToString(), 
                        Message: e.Value)) ?? [])
                .Distinct()
                .ToList();
            
            _instrumentation.ValidationFailed(validationErrors.Count);
            return Result<SchemaValidationResult>.Success(new SchemaValidationResult(validationErrors));
        }
        catch (Exception ex)
        {
            _instrumentation.ValidationMalformedJson(ex.Message);
            return Error.Validation(ErrorCodes.JsonMalformed, new { ex.Message });
        }
    }
}
