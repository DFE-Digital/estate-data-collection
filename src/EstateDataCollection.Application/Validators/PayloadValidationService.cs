using System.Diagnostics;
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
) : IPayloadValidationService
{
    private readonly ValidationOptions _options = options?.Value ?? new ValidationOptions();

    public IReadOnlyList<ValidationEngineDescriptor> GetRegisteredEngines()
    {
        var engines = new List<ValidationEngineDescriptor>
        {
            GetSchemaEngineDescriptor()
        };

        var ruleEngines = logicValidator.GetRegisteredEngines();
        if (ruleEngines is { Count: > 0 })
        {
            engines.AddRange(ruleEngines);
        }

        engines.Add(PayloadIdentifierExtractor.Descriptor);
        return engines;
    }

    public async Task<Result<IReadOnlyList<ValidationError>>> ValidatePayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var report = await ValidatePayloadWithTraceAsync(rawPayloadJson, schema, isDraftUpdate, cancellationToken);
        return report.IsFailure ? report.Error : Result<IReadOnlyList<ValidationError>>.Success(report.Value.Errors);
    }

    public async Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var ambientScope = SubmissionExecutionScope.Current;
        using var ownedScope = ambientScope is null
            ? SubmissionExecutionScope.Start(
                isDraftUpdate ? "ValidateDraftPayload" : "ValidatePayload",
                instrumentation: processInstrumentation,
                initialCapacity: 3)
            : null;
        var scope = ambientScope ?? ownedScope!;

        var policy = schema.Metadata.EffectiveValidationPolicy;
        var allIssues = new List<ValidationError>();
        var engineTraces = new List<ValidationEngineTrace>();

        // Step 1: Structural JSON Schema Validation
        var schemaStart = Stopwatch.GetTimestamp();
        var schemaResult = await scope.ExecuteAsync(
            SubmissionProcessCatalog.JsonSchemaValidator,
            () => schemaValidator.ValidateAsync(rawPayloadJson, schema.Schema, cancellationToken),
            val =>
            {
                var count = val.Errors?.Count ?? 0;
                return new ProcessStepDetails(
                    Outcome: count == 0 ? SubmissionProcessOutcome.Succeeded : SubmissionProcessOutcome.ValidationErrorsFound,
                    ItemCount: count,
                    Summary: count == 0 ? "Schema structure valid" : $"{count} schema error(s)",
                    Errors: MapAuditIssues(val.Errors));
            });
        var schemaDurationMs = (long)Stopwatch.GetElapsedTime(schemaStart).TotalMilliseconds;

        if (schemaResult.IsFailure)
        {
            return schemaResult.Error;
        }

        var schemaErrorCount = schemaResult.Value.Errors?.Count ?? 0;
        engineTraces.Add(new ValidationEngineTrace(
            Descriptor: GetSchemaEngineDescriptor(),
            Outcome: schemaErrorCount == 0
                ? nameof(SubmissionProcessOutcome.Succeeded)
                : nameof(SubmissionProcessOutcome.ValidationErrorsFound),
            ItemsEvaluated: 1,
            ViolationCount: schemaErrorCount,
            DurationMs: schemaDurationMs,
            Summary: schemaErrorCount == 0 ? "Schema structure valid" : $"{schemaErrorCount} schema error(s)"));

        if (schemaResult.Value.Errors is { Count: > 0 } schemaErrors)
        {
            allIssues.AddRange(schemaErrors.Select(e => string.IsNullOrWhiteSpace(e.EngineId)
                ? e with { EngineId = "json-schema" }
                : e));
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
                const string skippedDraftSummary = "Skipped during draft update per ValidationOptions";
                scope.RecordSkipped(
                    SubmissionProcessCatalog.QualityRulesValidator,
                    itemCount: schema.Rules.Count,
                    summary: skippedDraftSummary,
                    engineOverride: engineList);

                AddSkippedRuleEngineTraces(engineTraces, schema.Rules, distinctEngines, skippedDraftSummary);
            }
            else if (allIssues.Count > 0)
            {
                const string skippedSchemaFailureSummary = "Skipped because structural JSON Schema validation failed";
                scope.RecordSkipped(
                    SubmissionProcessCatalog.QualityRulesValidator,
                    itemCount: schema.Rules.Count,
                    summary: skippedSchemaFailureSummary,
                    engineOverride: engineList);

                AddSkippedRuleEngineTraces(engineTraces, schema.Rules, distinctEngines, skippedSchemaFailureSummary);
            }
            else
            {
                var logicResult = await scope.ExecuteAsync(
                    SubmissionProcessCatalog.QualityRulesValidator,
                    () => logicValidator.ValidateAsync(rawPayloadJson, schema.Rules, cancellationToken),
                    val =>
                    {
                        var blockingRuleIssues = ValidationIssuesHelper.GetBlockingIssues(val.Errors, policy.FailOnMinimumSeverity);
                        var totalIssueCount = val.Errors?.Count ?? 0;
                        return new ProcessStepDetails(
                            Outcome: blockingRuleIssues.Count == 0
                                ? SubmissionProcessOutcome.Succeeded
                                : SubmissionProcessOutcome.ValidationErrorsFound,
                            ItemCount: schema.Rules.Count,
                            Summary: totalIssueCount == 0
                                ? $"Evaluated {schema.Rules.Count} quality rule(s) with 0 failures"
                                : blockingRuleIssues.Count == 0
                                    ? $"Evaluated {schema.Rules.Count} quality rule(s); {totalIssueCount} advisory/warning issue(s)"
                                    : $"Evaluated {schema.Rules.Count} quality rule(s); {blockingRuleIssues.Count} blocking violation(s)",
                            Errors: MapAuditIssues(val.Errors));
                    },
                    err => new ProcessStepDetails(
                        Outcome: SubmissionProcessOutcome.Failed,
                        ItemCount: schema.Rules.Count,
                        Summary: err.Description ?? err.Code,
                        ErrorCode: err.Code),
                    engineOverride: engineList);

                if (logicResult.IsFailure)
                {
                    return logicResult.Error;
                }

                if (logicResult.Value.EngineTraces is { Count: > 0 } ruleTraces)
                {
                    engineTraces.AddRange(ruleTraces);
                }

                if (logicResult.Value.Errors is { Count: > 0 } logicErrors)
                {
                    allIssues.AddRange(logicErrors);
                }
            }
        }

        // Step 3: Establishment Identifier Extraction Check
        PayloadIdentifierExtractor.ExtractionEvaluation? identifierEval = null;
        var shouldCheckIdentifier = !isDraftUpdate || _options.ValidateIdentifierOnDraftUpdate;
        if (shouldCheckIdentifier)
        {
            var idStart = Stopwatch.GetTimestamp();
            identifierEval = scope.ExecuteSync(
                SubmissionProcessCatalog.IdentifierExtractor,
                () => PayloadIdentifierExtractor.Evaluate(rawPayloadJson, schema, _options),
                eval =>
                {
                    if (!eval.IsValid && eval.ValidationError is not null)
                    {
                        return new ProcessStepDetails(
                            Outcome: SubmissionProcessOutcome.ValidationErrorsFound,
                            Summary: eval.ValidationError.Message,
                            ErrorCode: eval.FailureError?.Code,
                            Errors:
                            [
                                new SubmissionAuditValidationIssue(
                                    eval.ValidationError.PropertyPath,
                                    eval.ValidationError.Message,
                                    eval.ValidationError.RuleId ?? eval.FailureError?.Code,
                                    eval.ValidationError.Severity)
                            ]);
                    }

                    var idLabel = eval.Identifier is { } id
                        ? $"{id.Type}={id.Value}"
                        : "Not required by schema";

                    return new ProcessStepDetails(
                        Outcome: SubmissionProcessOutcome.Succeeded,
                        Summary: idLabel);
                });
            var idDurationMs = (long)Stopwatch.GetElapsedTime(idStart).TotalMilliseconds;

            if (!identifierEval.IsEnabled)
            {
                engineTraces.Add(new ValidationEngineTrace(
                    Descriptor: PayloadIdentifierExtractor.Descriptor,
                    Outcome: nameof(SubmissionProcessOutcome.Skipped),
                    ItemsEvaluated: 0,
                    ViolationCount: 0,
                    DurationMs: idDurationMs,
                    Summary: "Identifier extraction disabled by schema"));
            }
            else
            {
                var idSummary = !identifierEval.IsValid && identifierEval.ValidationError is not null
                    ? identifierEval.ValidationError.Message
                    : identifierEval.Identifier is { } id
                        ? $"{id.Type}={id.Value}"
                        : "Not required by schema";

                engineTraces.Add(new ValidationEngineTrace(
                    Descriptor: PayloadIdentifierExtractor.Descriptor,
                    Outcome: identifierEval.IsValid
                        ? nameof(SubmissionProcessOutcome.Succeeded)
                        : nameof(SubmissionProcessOutcome.ValidationErrorsFound),
                    ItemsEvaluated: 1,
                    ViolationCount: identifierEval.IsValid ? 0 : 1,
                    DurationMs: idDurationMs,
                    Summary: idSummary));
            }

            if (!identifierEval.IsValid && identifierEval.ValidationError is not null)
            {
                var normalizedIdentifierPath = PayloadIdentifierExtractor.NormalisePathForComparison(identifierEval.ValidationError.PropertyPath);
                var alreadyReportedOnSamePath = allIssues.Any(e =>
                    e.Severity.IsAtOrAbove(policy.FailOnMinimumSeverity) &&
                    string.Equals(PayloadIdentifierExtractor.NormalisePathForComparison(e.PropertyPath), normalizedIdentifierPath, StringComparison.OrdinalIgnoreCase));

                if (!alreadyReportedOnSamePath)
                {
                    var idError = string.IsNullOrWhiteSpace(identifierEval.ValidationError.EngineId)
                        ? identifierEval.ValidationError with { EngineId = "identifier-extraction" }
                        : identifierEval.ValidationError;
                    allIssues.Add(idError);
                }
            }
        }
        else
        {
            engineTraces.Add(new ValidationEngineTrace(
                Descriptor: PayloadIdentifierExtractor.Descriptor,
                Outcome: nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: 0,
                ViolationCount: 0,
                DurationMs: 0,
                Summary: "Skipped during draft update per ValidationOptions"));
        }

        var groupedIssues = ValidationIssuesHelper.GroupBySeverity(allIssues);
        var blockingIssues = ValidationIssuesHelper.GetBlockingIssues(allIssues, policy.FailOnMinimumSeverity);

        return new PayloadValidationReport(
            Errors: blockingIssues,
            ProcessesExecuted: scope.Processes.ToArray(),
            IdentifierEvaluation: identifierEval,
            EngineTraces: engineTraces,
            Issues: groupedIssues,
            AllIssues: allIssues);
    }

    private static IReadOnlyList<SubmissionAuditValidationIssue>? MapAuditIssues(IReadOnlyList<ValidationError>? validationErrors)
    {
        return validationErrors is { Count: > 0 }
            ? validationErrors
                .Select(e => new SubmissionAuditValidationIssue(e.PropertyPath, e.Message, e.RuleId, e.Severity))
                .ToArray()
            : null;
    }

    private void AddSkippedRuleEngineTraces(
        List<ValidationEngineTrace> engineTraces,
        IReadOnlyList<Validators.Logic.BusinessRule> rules,
        string[] distinctEngines,
        string summary)
    {
        foreach (var engineId in distinctEngines)
        {
            var countForEngine = rules.Count(r =>
                string.Equals(r.ResolveEngine(_options.DefaultEngine), engineId, StringComparison.OrdinalIgnoreCase));

            engineTraces.Add(new ValidationEngineTrace(
                Descriptor: ResolveRuleEngineDescriptor(engineId),
                Outcome: nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: countForEngine,
                ViolationCount: 0,
                DurationMs: 0,
                Summary: summary));
        }
    }

    private ValidationEngineDescriptor GetSchemaEngineDescriptor()
    {
        return schemaValidator.Descriptor ?? new ValidationEngineDescriptor(
            "json-schema",
            "JSON Schema Validator",
            ValidationEngineKinds.StructuralSchema,
            "Structural JSON Schema validator.");
    }

    private ValidationEngineDescriptor ResolveRuleEngineDescriptor(string engineId)
    {
        var registered = logicValidator.GetRegisteredEngines();
        if (registered is not null)
        {
            foreach (var descriptor in registered)
            {
                if (string.Equals(descriptor.EngineId, engineId, StringComparison.OrdinalIgnoreCase))
                {
                    return descriptor;
                }
            }
        }

        return new ValidationEngineDescriptor(
            EngineId: engineId,
            DisplayName: engineId,
            Kind: ValidationEngineKinds.QualityRule,
            Description: $"Quality rule engine '{engineId}'.");
    }
}
