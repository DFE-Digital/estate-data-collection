using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.Validators;

public sealed class PayloadValidationService(
    ISchemaValidator schemaValidator,
    IBusinessLogicValidator logicValidator,
    IOptions<ValidationOptions>? options = null
) : IPayloadValidationService
{
    private readonly ValidationOptions _options = options?.Value ?? new ValidationOptions();

    public async Task<Result<IReadOnlyList<ValidationError>>> ValidatePayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Structural JSON Schema Validation
        var schemaResult = await schemaValidator.ValidateAsync(rawPayloadJson, schema.Schema, cancellationToken);
        if (schemaResult.IsFailure)
        {
            return schemaResult.Error;
        }

        var errors = new List<ValidationError>();
        if (schemaResult.Value.Errors is { Count: > 0 })
        {
            errors.AddRange(schemaResult.Value.Errors);
        }

        var shouldRunRules = !isDraftUpdate || _options.RunQualityRulesOnDraftUpdate;

        // Step 2: Quality / Business Rule Validation (when structural validation passed and rules exist)
        if (shouldRunRules && errors.Count == 0 && schema.Rules is { Count: > 0 })
        {
            var logicResult = await logicValidator.ValidateAsync(rawPayloadJson, schema.Rules, cancellationToken);
            if (logicResult.IsFailure)
            {
                return logicResult.Error;
            }

            if (logicResult.Value.Errors is { Count: > 0 })
            {
                errors.AddRange(logicResult.Value.Errors);
            }
        }

        // Step 3: School / Establishment Identifier Extraction Check
        var shouldCheckIdentifier = !isDraftUpdate || _options.ValidateIdentifierOnDraftUpdate;
        if (shouldCheckIdentifier)
        {
            var identifierEval = PayloadIdentifierExtractor.Evaluate(rawPayloadJson, schema, _options);
            if (!identifierEval.IsValid && identifierEval.ValidationError is not null)
            {
                var normalizedIdentifierPath = NormalizePointerForDedupe(identifierEval.ValidationError.PropertyPath);
                var alreadyReportedOnSamePath = errors.Any(e =>
                    string.Equals(NormalizePointerForDedupe(e.PropertyPath), normalizedIdentifierPath, StringComparison.OrdinalIgnoreCase));

                if (!alreadyReportedOnSamePath)
                {
                    errors.Add(identifierEval.ValidationError);
                }
            }
        }

        return errors;
    }

    private static string NormalizePointerForDedupe(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimStart('#').TrimEnd('/');
}
