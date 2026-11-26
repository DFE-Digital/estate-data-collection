using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators.Workflow.Steps;

public sealed class QualityRulesWorkflowStep(
    IBusinessLogicValidator logicValidator
) : IWorkflowStep
{
    public string StepId => WorkflowStepIds.QualityRules;

    public SubmissionProcessDescriptor ProcessDescriptor => SubmissionProcessCatalog.QualityRulesValidator;

    public WorkflowEngineDescriptor EngineDescriptor =>
        new(
            WorkflowStepIds.QualityRules,
            WorkflowMessages.QualityRulesCoordinatorDisplayName,
            ValidationEngineKinds.QualityRule,
            WorkflowMessages.QualityRulesCoordinatorDescription);

    public async Task<Result<WorkflowStepOutcome>> ExecuteAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Schema.Rules is not { Count: > 0 } rules)
        {
            return new WorkflowStepOutcome(Succeeded: true, Summary: WorkflowMessages.NoQualityRulesConfigured);
        }

        var distinctEngines = rules
            .Select(r => r.ResolveEngine(context.Options.DefaultEngine))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var engineList = string.Join(", ", distinctEngines);

        var shouldRunRules = !context.IsDraftUpdate || context.Options.RunQualityRulesOnDraftUpdate;
        if (!shouldRunRules)
        {
            const string skippedDraftSummary = WorkflowMessages.SkippedDuringDraftUpdateDetailed;
            RecordSkippedInternal(context, rules, distinctEngines, engineList, skippedDraftSummary);
            return new WorkflowStepOutcome(Succeeded: true, Summary: skippedDraftSummary);
        }

        var logicResult = await SubmissionExecutionScope.StepAsync(
            ProcessDescriptor,
            () => context.AmbientContext.Count > 0 || context.Policy.NumericToleranceOverride.HasValue
                ? logicValidator.ValidateAsync(
                    context.RawPayloadJson,
                    rules,
                    context.AmbientContext.Count > 0 ? context.AmbientContext : null,
                    context.EffectiveNumericTolerance,
                    cancellationToken)
                : logicValidator.ValidateAsync(
                    context.RawPayloadJson,
                    rules,
                    cancellationToken),
            val =>
            {
                var blockingRuleIssues = ValidationIssuesHelper.GetBlockingIssues(val.Errors, context.Policy.FailOnMinimumSeverity);
                var totalIssueCount = val.Errors?.Count ?? 0;
                return new ProcessStepDetails(
                    Outcome: blockingRuleIssues.Count == 0
                        ? SubmissionProcessOutcome.Succeeded
                        : SubmissionProcessOutcome.ValidationErrorsFound,
                    ItemCount: rules.Count,
                    Summary: totalIssueCount == 0
                        ? WorkflowMessages.EvaluatedQualityRulesZeroFailures(rules.Count)
                        : blockingRuleIssues.Count == 0
                            ? WorkflowMessages.EvaluatedQualityRulesAdvisory(rules.Count, totalIssueCount)
                            : WorkflowMessages.EvaluatedQualityRulesBlocking(rules.Count, blockingRuleIssues.Count),
                    Errors: MapAuditIssues(val.Errors));
            },
            err => new ProcessStepDetails(
                Outcome: SubmissionProcessOutcome.Failed,
                ItemCount: rules.Count,
                Summary: err.Description ?? err.Code,
                ErrorCode: err.Code),
            engineOverride: engineList);

        if (logicResult.IsFailure)
        {
            return logicResult.Error;
        }

        if (logicResult.Value.EngineTraces is { Count: > 0 } ruleTraces)
        {
            context.EngineTraces.AddRange(ruleTraces);
        }

        if (logicResult.Value.Errors is { Count: > 0 } logicErrors)
        {
            context.AllIssues.AddRange(logicErrors);
        }

        var hasBlocking = ValidationIssuesHelper.HasIssuesAtOrAbove(
            logicResult.Value.Errors ?? [],
            context.Policy.FailOnMinimumSeverity);

        return new WorkflowStepOutcome(Succeeded: !hasBlocking);
    }

    public void RecordSkipped(WorkflowContext context, string reason)
    {
        if (context.Schema.Rules is not { Count: > 0 } rules)
        {
            return;
        }

        var distinctEngines = rules
            .Select(r => r.ResolveEngine(context.Options.DefaultEngine))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var engineList = string.Join(", ", distinctEngines);
        RecordSkippedInternal(context, rules, distinctEngines, engineList, reason);
    }

    private void RecordSkippedInternal(
        WorkflowContext context,
        IReadOnlyList<Logic.BusinessRule> rules,
        string[] distinctEngines,
        string engineList,
        string summary)
    {
        SubmissionExecutionScope.Current?.RecordSkipped(
            ProcessDescriptor,
            itemCount: rules.Count,
            summary: summary,
            engineOverride: engineList);

        foreach (var engineId in distinctEngines)
        {
            var countForEngine = rules.Count(r =>
                string.Equals(r.ResolveEngine(context.Options.DefaultEngine), engineId, StringComparison.OrdinalIgnoreCase));

            context.EngineTraces.Add(new ValidationEngineTrace(
                Descriptor: ResolveRuleEngineDescriptor(engineId),
                Outcome: nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: countForEngine,
                ViolationCount: 0,
                DurationMs: 0,
                Summary: summary));
        }
    }

    private WorkflowEngineDescriptor ResolveRuleEngineDescriptor(string engineId)
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

        return new WorkflowEngineDescriptor(
            EngineId: engineId,
            DisplayName: engineId,
            Kind: ValidationEngineKinds.QualityRule,
            Description: WorkflowMessages.QualityRuleEngineDescription(engineId));
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
