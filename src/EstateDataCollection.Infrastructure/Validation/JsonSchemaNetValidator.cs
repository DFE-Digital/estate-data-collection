using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using Json.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Infrastructure.Validation;

public class JsonSchemaNetValidator(
    JsonSchemaNetValidatorInstrumentation? instrumentation = null
) : ISchemaValidator
{
    private readonly JsonSchemaNetValidatorInstrumentation _instrumentation =
        instrumentation ?? new JsonSchemaNetValidatorInstrumentation(NullLogger<JsonSchemaNetValidator>.Instance);

    private static readonly EvaluationOptions EvaluationOpts = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true
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
            
            var result = schema.Evaluate(node, EvaluationOpts);

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
