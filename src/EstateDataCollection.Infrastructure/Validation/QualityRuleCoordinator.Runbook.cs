using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Infrastructure.Validation;

public partial class QualityRuleCoordinator
{
    private static IReadOnlyList<ResolvedRuleEntry> SortRulesForRunbook(IReadOnlyList<ResolvedRuleEntry> entries)
    {
        if (entries.Count <= 1)
        {
            return entries;
        }

        var idToIndices = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            var id = entries[i].Rule.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!idToIndices.TryGetValue(id, out var list))
            {
                list = [];
                idToIndices[id] = list;
            }

            list.Add(i);
        }

        var dependents = new List<int>[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            dependents[i] = [];
        }

        var inDegree = new int[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var deps = entries[i].Rule.DependsOn;
            if (deps is not { Count: > 0 })
            {
                continue;
            }

            var seenPrereqs = new HashSet<int>();
            foreach (var depId in deps)
            {
                if (string.IsNullOrWhiteSpace(depId) || !idToIndices.TryGetValue(depId.Trim(), out var prereqIndices))
                {
                    continue;
                }

                foreach (var prereqIdx in prereqIndices)
                {
                    if (prereqIdx == i || !seenPrereqs.Add(prereqIdx))
                    {
                        continue;
                    }

                    dependents[prereqIdx].Add(i);
                    inDegree[i]++;
                }
            }
        }

        var queue = new PriorityQueue<int, (int Stage, int Order, int OriginalIndex)>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (inDegree[i] == 0)
            {
                var r = entries[i].Rule;
                queue.Enqueue(i, (r.Stage, r.Order, entries[i].OriginalIndex));
            }
        }

        var sorted = new List<ResolvedRuleEntry>(entries.Count);
        var visited = new bool[entries.Count];

        while (queue.TryDequeue(out var idx, out _))
        {
            if (visited[idx])
            {
                continue;
            }

            visited[idx] = true;
            sorted.Add(entries[idx]);

            foreach (var depIdx in dependents[idx])
            {
                inDegree[depIdx]--;
                if (inDegree[depIdx] == 0)
                {
                    var depRule = entries[depIdx].Rule;
                    queue.Enqueue(depIdx, (depRule.Stage, depRule.Order, entries[depIdx].OriginalIndex));
                }
            }
        }

        if (sorted.Count < entries.Count)
        {
            var remaining = new List<ResolvedRuleEntry>(entries.Count - sorted.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                if (!visited[i])
                {
                    remaining.Add(entries[i]);
                }
            }

            remaining.Sort((a, b) =>
            {
                var cmp = a.Rule.Stage.CompareTo(b.Rule.Stage);
                if (cmp != 0)
                {
                    return cmp;
                }

                cmp = a.Rule.Order.CompareTo(b.Rule.Order);
                return cmp != 0 ? cmp : a.OriginalIndex.CompareTo(b.OriginalIndex);
            });

            sorted.AddRange(remaining);
        }

        return sorted;
    }

    private static bool HasFailedDependency(
        BusinessRule rule,
        HashSet<string> failedRuleIds,
        out string? failedDependencyId)
    {
        if (rule.DependsOn is { Count: > 0 } && failedRuleIds.Count > 0)
        {
            foreach (var dep in rule.DependsOn)
            {
                if (!string.IsNullOrWhiteSpace(dep) && failedRuleIds.Contains(dep.Trim()))
                {
                    failedDependencyId = dep.Trim();
                    return true;
                }
            }
        }

        failedDependencyId = null;
        return false;
    }

    private WorkflowEngineDescriptor ResolveDescriptorOrFallback(string engineName)
    {
        if (_engines.TryGetValue(engineName, out var registered))
        {
            return registered.Descriptor;
        }

        return new WorkflowEngineDescriptor(
            EngineId: engineName,
            DisplayName: engineName,
            Kind: ValidationEngineKinds.QualityRule,
            Description: $"Unregistered or unsupported quality rule engine '{engineName}'.");
    }

    private bool TryResolveEngine(string engineName, out IQualityRuleEngine? engine, out string? errorMessage)
    {
        engine = null;
        errorMessage = null;

        if (!_enabledEngines.Contains(engineName))
        {
            var supported = string.Join(", ", _enabledEngines.OrderBy(x => x));
            errorMessage = $"Quality rule engine '{engineName}' is not enabled or supported. Supported engines: {supported}.";
            return false;
        }

        if (!_engines.TryGetValue(engineName, out engine))
        {
            var registered = string.Join(", ", _engines.Keys.OrderBy(x => x));
            errorMessage = $"Quality rule engine '{engineName}' is not registered. Registered engines: {registered}.";
            return false;
        }

        return true;
    }

    private sealed record ResolvedRuleEntry(BusinessRule Rule, IQualityRuleEngine Engine, int OriginalIndex);

    private sealed class EngineTraceAccumulator(WorkflowEngineDescriptor descriptor)
    {
        public WorkflowEngineDescriptor Descriptor { get; } = descriptor;
        public int RulesEvaluatedCount { get; set; }
        public int SkippedUnknownCount { get; set; }
        public int SkippedRunbookCount { get; set; }
        public int ViolationCount { get; set; }
        public int AdvisoryInfoCount { get; set; }
        public long ElapsedTicks { get; set; }
        public bool HasFailure { get; set; }
        public string? LastErrorSummary { get; set; }

        public ValidationEngineTrace ToTrace()
        {
            var durationMs = (long)Stopwatch.GetElapsedTime(0, ElapsedTicks).TotalMilliseconds;
            var totalSkipped = SkippedUnknownCount + SkippedRunbookCount;

            if (RulesEvaluatedCount == 0 && totalSkipped > 0)
            {
                return new ValidationEngineTrace(
                    Descriptor,
                    nameof(SubmissionProcessOutcome.Skipped),
                    ItemsEvaluated: totalSkipped,
                    ViolationCount: 0,
                    DurationMs: durationMs,
                    Summary: LastErrorSummary ?? $"Skipped {totalSkipped} rule(s)");
            }

            string outcome;
            if (HasFailure)
            {
                outcome = nameof(SubmissionProcessOutcome.Failed);
            }
            else if (ViolationCount > 0)
            {
                outcome = nameof(SubmissionProcessOutcome.ValidationErrorsFound);
            }
            else
            {
                outcome = nameof(SubmissionProcessOutcome.Succeeded);
            }

            var skipSuffix = totalSkipped > 0 ? $"; {totalSkipped} skipped" : string.Empty;
            var infoSuffix = AdvisoryInfoCount > 0 ? $"; {AdvisoryInfoCount} info notice(s)" : string.Empty;

            var summary = ViolationCount == 0
                ? $"Evaluated {RulesEvaluatedCount} rule(s) with 0 violations{skipSuffix}{infoSuffix}"
                : LastErrorSummary is not null && HasFailure
                    ? LastErrorSummary
                    : $"Evaluated {RulesEvaluatedCount} rule(s); {ViolationCount} violation(s){skipSuffix}{infoSuffix}";

            return new ValidationEngineTrace(
                Descriptor,
                outcome,
                ItemsEvaluated: RulesEvaluatedCount,
                ViolationCount: ViolationCount,
                DurationMs: durationMs,
                Summary: summary);
        }
    }
}
