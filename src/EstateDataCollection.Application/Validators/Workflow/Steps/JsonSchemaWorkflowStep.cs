using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators.Workflow.Steps;

public sealed class JsonSchemaWorkflowStep(
    ISchemaValidator schemaValidator
) : IWorkflowStep
{
    public string StepId => WorkflowStepIds.JsonSchema;

    public SubmissionProcessDescriptor ProcessDescriptor => SubmissionProcessCatalog.JsonSchemaValidator;

    public WorkflowEngineDescriptor EngineDescriptor =>
        schemaValidator.Descriptor ?? new WorkflowEngineDescriptor(
            "json-schema",
            "JSON Schema Validator",
            ValidationEngineKinds.StructuralSchema,
            "Structural JSON Schema validator.");

    public async Task<Result<WorkflowStepOutcome>> ExecuteAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var schemaStart = Stopwatch.GetTimestamp();
        var schemaResult = await SubmissionExecutionScope.StepAsync(
            ProcessDescriptor,
            () => schemaValidator.ValidateAsync(context.RawPayloadJson, context.Schema.Schema, cancellationToken),
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
        context.EngineTraces.Add(new ValidationEngineTrace(
            Descriptor: EngineDescriptor,
            Outcome: schemaErrorCount == 0
                ? nameof(SubmissionProcessOutcome.Succeeded)
                : nameof(SubmissionProcessOutcome.ValidationErrorsFound),
            ItemsEvaluated: 1,
            ViolationCount: schemaErrorCount,
            DurationMs: schemaDurationMs,
            Summary: schemaErrorCount == 0 ? "Schema structure valid" : $"{schemaErrorCount} schema error(s)"));

        if (schemaResult.Value.Errors is { Count: > 0 } schemaErrors)
        {
            context.AllIssues.AddRange(schemaErrors.Select(e => string.IsNullOrWhiteSpace(e.EngineId)
                ? e with { EngineId = WorkflowStepIds.JsonSchema }
                : e));
        }

        return new WorkflowStepOutcome(
            Succeeded: schemaErrorCount == 0,
            Summary: schemaErrorCount == 0 ? "Schema structure valid" : $"{schemaErrorCount} schema error(s)");
    }

    private static IReadOnlyList<SubmissionAuditValidationIssue>? MapAuditIssues(IReadOnlyList<ValidationError>? validationErrors)
    {
        return validationErrors is { Count: > 0 }
            ? validationErrors
                .Select(e => new SubmissionAuditValidationIssue(e.PropertyPath, e.Message, e.RuleId, e.Severity))
                .ToArray()
            : null;
    }
}
