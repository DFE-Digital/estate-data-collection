using System.Diagnostics;
using System.Text.Json;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators;

public sealed partial class SchemaVerificationService
{
    private void RecordRuleSyntaxErrors(
        string rawRulesJson,
        IReadOnlyList<ValidationMessage> syntaxErrors,
        long elapsedTicks,
        Func<string, EngineRunState> getOrCreateRuleTrace,
        IEnumerable<EngineRunState> registeredStates)
    {
        List<BusinessRule>? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<List<BusinessRule>>(rawRulesJson, RuleJsonOptions);
        }
        catch
        {
            // Malformed JSON array — handled below by applying syntax error to registered rule engines.
        }

        if (parsed is { Count: > 0 })
        {
            var rulesByEngine = parsed
                .GroupBy(r => r.ResolveEngine(_validationOptions.DefaultEngine), StringComparer.OrdinalIgnoreCase);

            foreach (var group in rulesByEngine)
            {
                var state = getOrCreateRuleTrace(group.Key);
                state.RuleCount = group.Count();
                state.ItemsEvaluated += group.Count();
                state.ElapsedTicks += elapsedTicks;

                var ruleIds = group.Select(r => r.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var matchingErrors = syntaxErrors
                    .Where(e => !string.IsNullOrWhiteSpace(e.RuleId) && ruleIds.Contains(e.RuleId))
                    .ToList();

                if (matchingErrors.Count > 0)
                {
                    state.ViolationCount += matchingErrors.Count;
                    state.FirstIssue ??= matchingErrors[0].Message;
                }
            }

            if (syntaxErrors.All(e => string.IsNullOrWhiteSpace(e.RuleId)))
            {
                foreach (var state in registeredStates.Where(s => s.RuleCount > 0))
                {
                    state.ViolationCount += syntaxErrors.Count;
                    state.FirstIssue ??= syntaxErrors[0].Message;
                }
            }

            return;
        }

        var firstMessage = syntaxErrors[0].Message;
        foreach (var state in registeredStates)
        {
            state.ItemsEvaluated++;
            state.ElapsedTicks += elapsedTicks;
            state.ViolationCount += syntaxErrors.Count;
            state.FirstIssue ??= firstMessage;
        }
    }

    private void MergeRuleEngineTracesForValidFixture(
        BusinessRuleValidationResult validationResult,
        IReadOnlyList<BusinessRule> candidateRules,
        long fallbackTicks,
        Func<string, EngineRunState> getOrCreateRuleTrace)
    {
        if (validationResult.EngineTraces is { Count: > 0 } traces)
        {
            foreach (var trace in traces)
            {
                var state = getOrCreateRuleTrace(trace.Descriptor.EngineId);
                state.ItemsEvaluated += trace.ItemsEvaluated;
                state.ViolationCount += trace.ViolationCount;
                state.ExplicitDurationMs += trace.DurationMs;

                if (string.Equals(trace.Outcome, nameof(SubmissionProcessOutcome.Failed), StringComparison.Ordinal))
                {
                    state.HasFailure = true;
                    state.FirstIssue ??= trace.Summary;
                }
                else if (trace.ViolationCount > 0)
                {
                    state.FirstIssue ??= trace.Summary;
                }
            }

            return;
        }

        foreach (var group in candidateRules.GroupBy(
                     r => r.ResolveEngine(_validationOptions.DefaultEngine),
                     StringComparer.OrdinalIgnoreCase))
        {
            var state = getOrCreateRuleTrace(group.Key);
            state.ItemsEvaluated += group.Count();
            state.ElapsedTicks += fallbackTicks;
        }

        if (!validationResult.IsValid && validationResult.Errors is { Count: > 0 })
        {
            foreach (var state in candidateRules
                         .Select(r => r.ResolveEngine(_validationOptions.DefaultEngine))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Select(getOrCreateRuleTrace))
            {
                state.ViolationCount += validationResult.Errors.Count;
                state.FirstIssue ??= validationResult.Errors[0].Message;
            }
        }
    }

    private void MergeRuleEngineTracesForInvalidFixture(
        BusinessRuleValidationResult validationResult,
        IReadOnlyList<BusinessRule> candidateRules,
        long fallbackTicks,
        Func<string, EngineRunState> getOrCreateRuleTrace)
    {
        if (validationResult.EngineTraces is { Count: > 0 } traces)
        {
            foreach (var trace in traces)
            {
                var state = getOrCreateRuleTrace(trace.Descriptor.EngineId);
                state.ItemsEvaluated += trace.ItemsEvaluated;
                state.ExplicitDurationMs += trace.DurationMs;
                if (trace.ViolationCount > 0)
                {
                    state.InvalidTriggers++;
                }
            }

            return;
        }

        foreach (var group in candidateRules.GroupBy(
                     r => r.ResolveEngine(_validationOptions.DefaultEngine),
                     StringComparer.OrdinalIgnoreCase))
        {
            var state = getOrCreateRuleTrace(group.Key);
            state.ItemsEvaluated += group.Count();
            state.ElapsedTicks += fallbackTicks;
            if (!validationResult.IsValid)
            {
                state.InvalidTriggers++;
            }
        }
    }

    private static string? FormatIdentifierLabel(
        IdentifierSchemaAlignmentResult alignment,
        string effectiveType)
    {
        if (!alignment.IsEnabled)
        {
            return null;
        }

        var ptr = alignment.EffectiveConfig?.JsonPointer
                  ?? alignment.ResolvedPaths.FirstOrDefault()
                  ?? string.Empty;
        return $"Type '{effectiveType}' at '{ptr}' (required: {alignment.IsRequiredInSchema})";
    }

    private static IReadOnlyList<ValidationEngineTrace> BuildEngineTraces(
        EngineRunState schemaState,
        IEnumerable<EngineRunState> ruleStates,
        EngineRunState identifierState,
        EngineRunState consistencyState,
        bool hasSchema,
        bool hasRules,
        bool identifierEnabled,
        int validFixtureCount,
        int invalidFixtureCount,
        string? identifierLabel)
    {
        var traces = new List<ValidationEngineTrace>();

        if (!hasSchema && !schemaState.HasFailure && schemaState.ViolationCount == 0)
        {
            traces.Add(new ValidationEngineTrace(
                schemaState.Descriptor,
                nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: 0,
                ViolationCount: 0,
                DurationMs: 0,
                Summary: "No JSON Schema provided"));
        }
        else
        {
            var schemaOutcome = schemaState.HasFailure
                ? nameof(SubmissionProcessOutcome.Failed)
                : schemaState.ViolationCount > 0
                    ? nameof(SubmissionProcessOutcome.ValidationErrorsFound)
                    : nameof(SubmissionProcessOutcome.Succeeded);

            var schemaSummary = schemaState.HasFailure
                ? schemaState.FirstIssue ?? "Malformed JSON Schema"
                : schemaState.ViolationCount > 0
                    ? schemaState.FirstIssue ?? $"{schemaState.ViolationCount} schema error(s)"
                    : BuildFixtureSummary("Schema valid", validFixtureCount, invalidFixtureCount, schemaState.InvalidTriggers);

            traces.Add(new ValidationEngineTrace(
                schemaState.Descriptor,
                schemaOutcome,
                ItemsEvaluated: schemaState.ItemsEvaluated,
                ViolationCount: schemaState.ViolationCount,
                DurationMs: schemaState.TotalDurationMs,
                Summary: schemaSummary));
        }

        foreach (var ruleState in ruleStates)
        {
            if (ruleState.RuleCount == 0 && !ruleState.HasFailure && ruleState.ViolationCount == 0)
            {
                traces.Add(new ValidationEngineTrace(
                    ruleState.Descriptor,
                    nameof(SubmissionProcessOutcome.Skipped),
                    ItemsEvaluated: 0,
                    ViolationCount: 0,
                    DurationMs: ruleState.TotalDurationMs,
                    Summary: hasRules
                        ? $"No quality rules targeting '{ruleState.Descriptor.EngineId}'"
                        : "No quality rules configured"));
                continue;
            }

            var ruleOutcome = ruleState.HasFailure
                ? nameof(SubmissionProcessOutcome.Failed)
                : ruleState.ViolationCount > 0
                    ? nameof(SubmissionProcessOutcome.ValidationErrorsFound)
                    : nameof(SubmissionProcessOutcome.Succeeded);

            var ruleSummary = ruleState.HasFailure
                ? ruleState.FirstIssue ?? "Quality rule engine evaluation failed"
                : ruleState.ViolationCount > 0
                    ? ruleState.FirstIssue ?? $"{ruleState.RuleCount} rule(s); {ruleState.ViolationCount} violation(s)"
                    : BuildFixtureSummary(
                        $"{ruleState.RuleCount} rule(s) verified",
                        validFixtureCount,
                        invalidFixtureCount,
                        ruleState.InvalidTriggers);

            traces.Add(new ValidationEngineTrace(
                ruleState.Descriptor,
                ruleOutcome,
                ItemsEvaluated: ruleState.ItemsEvaluated,
                ViolationCount: ruleState.ViolationCount,
                DurationMs: ruleState.TotalDurationMs,
                Summary: ruleSummary));
        }

        if (!identifierEnabled && identifierState.ViolationCount == 0)
        {
            traces.Add(new ValidationEngineTrace(
                identifierState.Descriptor,
                nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: 0,
                ViolationCount: 0,
                DurationMs: identifierState.TotalDurationMs,
                Summary: hasSchema
                    ? "Identifier extraction disabled by schema"
                    : "No JSON Schema provided for identifier alignment"));
        }
        else
        {
            var idOutcome = identifierState.ViolationCount > 0
                ? nameof(SubmissionProcessOutcome.ValidationErrorsFound)
                : nameof(SubmissionProcessOutcome.Succeeded);

            var idSummary = identifierState.ViolationCount > 0
                ? identifierState.FirstIssue ?? $"{identifierState.ViolationCount} identifier error(s)"
                : BuildFixtureSummary(
                    identifierLabel ?? "Identifier config aligned",
                    validFixtureCount,
                    invalidFixtureCount,
                    identifierState.InvalidTriggers);

            traces.Add(new ValidationEngineTrace(
                identifierState.Descriptor,
                idOutcome,
                ItemsEvaluated: identifierState.ItemsEvaluated,
                ViolationCount: identifierState.ViolationCount,
                DurationMs: identifierState.TotalDurationMs,
                Summary: idSummary));
        }

        if (consistencyState.ItemsEvaluated == 0 && consistencyState.ViolationCount == 0 && consistencyState.WarningCount == 0)
        {
            traces.Add(new ValidationEngineTrace(
                consistencyState.Descriptor,
                nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: 0,
                ViolationCount: 0,
                DurationMs: consistencyState.TotalDurationMs,
                Summary: hasRules
                    ? "Rules did not parse cleanly for consistency analysis"
                    : "No quality rules configured"));
        }
        else
        {
            var consistencyOutcome = consistencyState.ViolationCount > 0
                ? nameof(SubmissionProcessOutcome.ValidationErrorsFound)
                : nameof(SubmissionProcessOutcome.Succeeded);

            var consistencySummary = consistencyState.ViolationCount > 0
                ? consistencyState.FirstIssue ?? $"{consistencyState.ViolationCount} contract consistency error(s)"
                : consistencyState.WarningCount > 0
                    ? $"Checked {consistencyState.ItemsEvaluated} rule(s); {consistencyState.WarningCount} warning(s)"
                    : $"Checked {consistencyState.ItemsEvaluated} rule(s); consistent with JSON Schema and DAG";

            traces.Add(new ValidationEngineTrace(
                consistencyState.Descriptor,
                consistencyOutcome,
                ItemsEvaluated: consistencyState.ItemsEvaluated,
                ViolationCount: consistencyState.ViolationCount,
                DurationMs: consistencyState.TotalDurationMs,
                Summary: consistencySummary));
        }

        return traces;
    }

    private static string BuildFixtureSummary(
        string prefix,
        int validFixtureCount,
        int invalidFixtureCount,
        int invalidTriggers)
    {
        var baseSummary = $"{prefix}; checked {validFixtureCount} valid and {invalidFixtureCount} invalid fixture(s)";
        return invalidTriggers > 0
            ? $"{baseSummary} (triggered {invalidTriggers} invalid fixture(s))"
            : baseSummary;
    }

    private sealed class EngineRunState(WorkflowEngineDescriptor descriptor)
    {
        public WorkflowEngineDescriptor Descriptor { get; } = descriptor;
        public int RuleCount { get; set; }
        public int ItemsEvaluated { get; set; }
        public int ViolationCount { get; set; }
        public int WarningCount { get; set; }
        public int InvalidTriggers { get; set; }
        public long ElapsedTicks { get; set; }
        public long ExplicitDurationMs { get; set; }
        public bool HasFailure { get; set; }
        public string? FirstIssue { get; set; }

        public long TotalDurationMs => (long)Stopwatch.GetElapsedTime(0, ElapsedTicks).TotalMilliseconds + ExplicitDurationMs;
    }
}
