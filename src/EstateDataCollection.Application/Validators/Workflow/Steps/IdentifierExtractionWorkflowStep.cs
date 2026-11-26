using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators.Workflow.Steps;

public sealed class IdentifierExtractionWorkflowStep : IWorkflowStep
{
    public string StepId => WorkflowStepIds.IdentifierExtraction;

    public SubmissionProcessDescriptor ProcessDescriptor => SubmissionProcessCatalog.IdentifierExtractor;

    public WorkflowEngineDescriptor EngineDescriptor => PayloadIdentifierExtractor.Descriptor;

    public Task<Result<WorkflowStepOutcome>> ExecuteAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var shouldCheckIdentifier = !context.IsDraftUpdate || context.Options.ValidateIdentifierOnDraftUpdate;
        if (!shouldCheckIdentifier)
        {
            context.EngineTraces.Add(new ValidationEngineTrace(
                Descriptor: EngineDescriptor,
                Outcome: nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: 0,
                ViolationCount: 0,
                DurationMs: 0,
                Summary: WorkflowMessages.SkippedDuringDraftUpdateDetailed));

            return Task.FromResult(Result<WorkflowStepOutcome>.Success(
                new WorkflowStepOutcome(Succeeded: true, Summary: WorkflowMessages.SkippedDuringDraftUpdateShort)));
        }

        var idStart = Stopwatch.GetTimestamp();
        var identifierEval = SubmissionExecutionScope.StepSync(
            ProcessDescriptor,
            () => PayloadIdentifierExtractor.Evaluate(context.RawPayloadJson, context.Schema, context.Options),
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
                    : WorkflowMessages.IdentifierNotRequiredBySchema;

                return new ProcessStepDetails(
                    Outcome: SubmissionProcessOutcome.Succeeded,
                    Summary: idLabel);
            });
        var idDurationMs = (long)Stopwatch.GetElapsedTime(idStart).TotalMilliseconds;

        context.IdentifierEvaluation = identifierEval;

        if (!identifierEval.IsEnabled)
        {
            context.EngineTraces.Add(new ValidationEngineTrace(
                Descriptor: EngineDescriptor,
                Outcome: nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: 0,
                ViolationCount: 0,
                DurationMs: idDurationMs,
                Summary: WorkflowMessages.IdentifierDisabledBySchema));
        }
        else
        {
            var idSummary = !identifierEval.IsValid && identifierEval.ValidationError is not null
                ? identifierEval.ValidationError.Message
                : identifierEval.Identifier is { } id
                    ? $"{id.Type}={id.Value}"
                    : WorkflowMessages.IdentifierNotRequiredBySchema;

            context.EngineTraces.Add(new ValidationEngineTrace(
                Descriptor: EngineDescriptor,
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
            var alreadyReportedOnSamePath = context.AllIssues.Any(e =>
                e.Severity.IsAtOrAbove(context.Policy.FailOnMinimumSeverity) &&
                string.Equals(PayloadIdentifierExtractor.NormalisePathForComparison(e.PropertyPath), normalizedIdentifierPath, StringComparison.OrdinalIgnoreCase));

            if (!alreadyReportedOnSamePath)
            {
                var idError = string.IsNullOrWhiteSpace(identifierEval.ValidationError.EngineId)
                    ? identifierEval.ValidationError with { EngineId = WorkflowStepIds.IdentifierExtraction }
                    : identifierEval.ValidationError;
                context.AllIssues.Add(idError);
            }
        }

        return Task.FromResult(Result<WorkflowStepOutcome>.Success(
            new WorkflowStepOutcome(Succeeded: identifierEval.IsValid)));
    }
}
