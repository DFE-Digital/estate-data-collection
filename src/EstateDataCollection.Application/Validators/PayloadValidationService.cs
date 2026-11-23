using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.Validators;

public sealed class PayloadValidationService(
    ISchemaValidator schemaValidator,
    IBusinessLogicValidator logicValidator,
    IOptions<ValidationOptions>? options = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
) : IPayloadValidationService, IScopedPayloadValidationService
{
    private readonly ValidationOptions _options = options?.Value ?? new ValidationOptions();

    public async Task<Result<IReadOnlyList<ValidationError>>> ValidatePayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var report = await ValidatePayloadWithTraceAsync(rawPayloadJson, schema, isDraftUpdate, cancellationToken);
        return report.IsFailure ? report.Error : Result<IReadOnlyList<ValidationError>>.Success(report.Value.Errors);
    }

    public Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        return ValidatePayloadWithTraceCoreAsync(rawPayloadJson, schema, isDraftUpdate, executionScope: null, cancellationToken);
    }

    Task<Result<PayloadValidationReport>> IScopedPayloadValidationService.ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate,
        SubmissionExecutionScope executionScope,
        CancellationToken cancellationToken)
    {
        return ValidatePayloadWithTraceCoreAsync(rawPayloadJson, schema, isDraftUpdate, executionScope, cancellationToken);
    }

    public Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate,
        SubmissionExecutionScope? executionScope,
        CancellationToken cancellationToken = default)
    {
        return ValidatePayloadWithTraceCoreAsync(rawPayloadJson, schema, isDraftUpdate, executionScope, cancellationToken);
    }

    private async Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceCoreAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate,
        SubmissionExecutionScope? executionScope,
        CancellationToken cancellationToken)
    {
        using var ownedScope = executionScope is null
            ? SubmissionExecutionScope.Start(
                isDraftUpdate ? "ValidateDraftPayload" : "ValidatePayload",
                instrumentation: processInstrumentation,
                initialCapacity: 3)
            : null;
        var scope = executionScope ?? ownedScope!;

        var errors = new List<ValidationError>();

        // Step 1: Structural JSON Schema Validation
        var schemaResult = await scope.ExecuteAsync(
            SubmissionProcessCatalog.JsonSchemaValidator,
            () => schemaValidator.ValidateAsync(rawPayloadJson, schema.Schema, cancellationToken),
            val =>
            {
                var count = val.Errors?.Count ?? 0;
                return new ProcessStepDetails(
                    Outcome: count == 0 ? SubmissionProcessOutcome.Succeeded : SubmissionProcessOutcome.ValidationErrorsFound,
                    ItemCount: count,
                    Summary: count == 0 ? "Schema structure valid" : $"{count} schema error(s)");
            });

        if (schemaResult.IsFailure)
        {
            return schemaResult.Error;
        }

        if (schemaResult.Value.Errors is { Count: > 0 } schemaErrors)
        {
            errors.AddRange(schemaErrors);
        }

        var shouldRunRules = !isDraftUpdate || _options.RunQualityRulesOnDraftUpdate;

        // Step 2: Quality / Business Rule Validation (when structural validation passed and rules exist)
        if (schema.Rules is { Count: > 0 })
        {
            var distinctEngines = schema.Rules
                .Select(r => r.ResolveEngine(_options.DefaultEngine))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var engineList = string.Join(", ", distinctEngines);

            if (!shouldRunRules)
            {
                scope.RecordSkipped(
                    SubmissionProcessCatalog.QualityRulesValidator,
                    itemCount: schema.Rules.Count,
                    summary: "Skipped during draft update per ValidationOptions",
                    engineOverride: engineList);
            }
            else if (errors.Count > 0)
            {
                scope.RecordSkipped(
                    SubmissionProcessCatalog.QualityRulesValidator,
                    itemCount: schema.Rules.Count,
                    summary: "Skipped because structural JSON Schema validation failed",
                    engineOverride: engineList);
            }
            else
            {
                var logicResult = await scope.ExecuteAsync(
                    SubmissionProcessCatalog.QualityRulesValidator,
                    () => logicValidator.ValidateAsync(rawPayloadJson, schema.Rules, cancellationToken),
                    val =>
                    {
                        var violationCount = val.Errors?.Count ?? 0;
                        return new ProcessStepDetails(
                            Outcome: violationCount == 0
                                ? SubmissionProcessOutcome.Succeeded
                                : SubmissionProcessOutcome.ValidationErrorsFound,
                            ItemCount: schema.Rules.Count,
                            Summary: violationCount == 0
                                ? $"Evaluated {schema.Rules.Count} quality rule(s) with 0 failures"
                                : $"Evaluated {schema.Rules.Count} quality rule(s); {violationCount} violation(s)");
                    },
                    err => new ProcessStepDetails(
                        Outcome: SubmissionProcessOutcome.Failed,
                        ItemCount: schema.Rules.Count,
                        Summary: err.Description ?? err.Code),
                    engineOverride: engineList);

                if (logicResult.IsFailure)
                {
                    return logicResult.Error;
                }

                if (logicResult.Value.Errors is { Count: > 0 } logicErrors)
                {
                    errors.AddRange(logicErrors);
                }
            }
        }

        // Step 3: School / Establishment Identifier Extraction Check
        PayloadIdentifierExtractor.ExtractionEvaluation? identifierEval = null;
        var shouldCheckIdentifier = !isDraftUpdate || _options.ValidateIdentifierOnDraftUpdate;
        if (shouldCheckIdentifier)
        {
            identifierEval = scope.ExecuteSync(
                SubmissionProcessCatalog.IdentifierExtractor,
                () => PayloadIdentifierExtractor.Evaluate(rawPayloadJson, schema, _options),
                eval =>
                {
                    if (!eval.IsValid && eval.ValidationError is not null)
                    {
                        return new ProcessStepDetails(
                            Outcome: SubmissionProcessOutcome.ValidationErrorsFound,
                            Summary: eval.ValidationError.Message);
                    }

                    var idLabel = eval.Identifier is { } id
                        ? $"{id.Type}={id.Value}"
                        : "Not required by schema";

                    return new ProcessStepDetails(
                        Outcome: SubmissionProcessOutcome.Succeeded,
                        Summary: idLabel);
                });

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

        return new PayloadValidationReport(errors, scope.Processes.ToArray(), identifierEval);
    }

    private static string NormalizePointerForDedupe(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimStart('#').TrimEnd('/');
    }
}
