using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators;

public sealed partial class ContractConsistencyAnalyser
{
    private static void AnalyseRunbookGraph(
        IReadOnlyList<BusinessRule> rules,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding)
    {
        var ruleMap = new Dictionary<string, BusinessRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules)
        {
            if (!string.IsNullOrWhiteSpace(r.Id))
            {
                ruleMap.TryAdd(r.Id.Trim(), r);
            }
        }

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || rule.DependsOn is not { Count: > 0 })
            {
                continue;
            }

            var ruleId = rule.Id.Trim();
            foreach (var rawDep in rule.DependsOn)
            {
                if (string.IsNullOrWhiteSpace(rawDep))
                {
                    continue;
                }

                var depId = rawDep.Trim();
                if (!ruleMap.TryGetValue(depId, out var depRule))
                {
                    addFinding(
                        RuleSeverity.Error,
                        ToJsonPointer(rule.Path),
                        ConsistencyAnalyserMessages.MissingDependency(ruleId, depId),
                        ruleId,
                        new Dictionary<string, object?> { ["check"] = "missing_dependency", ["dependsOn"] = depId });
                    continue;
                }

                if (depRule.Stage > rule.Stage)
                {
                    addFinding(
                        RuleSeverity.Error,
                        ToJsonPointer(rule.Path),
                        ConsistencyAnalyserMessages.LaterStageDependency(ruleId, rule.Stage, depId, depRule.Stage),
                        ruleId,
                        new Dictionary<string, object?> { ["check"] = "later_stage_dependency", ["dependsOn"] = depId });
                }
            }
        }

        // 3-color DFS cycle detection: 0 = unvisited, 1 = visiting, 2 = visited
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                continue;
            }

            var id = rule.Id.Trim();
            if (!state.ContainsKey(id))
            {
                DfsCycleCheck(id, ruleMap, state, stack, addFinding);
            }
        }
    }

    private static void DfsCycleCheck(
        string currentId,
        Dictionary<string, BusinessRule> ruleMap,
        Dictionary<string, int> state,
        List<string> stack,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding)
    {
        state[currentId] = 1;
        stack.Add(currentId);

        if (ruleMap.TryGetValue(currentId, out var rule) && rule.DependsOn is { Count: > 0 })
        {
            foreach (var rawDep in rule.DependsOn)
            {
                if (string.IsNullOrWhiteSpace(rawDep))
                {
                    continue;
                }

                var depId = rawDep.Trim();
                if (!ruleMap.ContainsKey(depId))
                {
                    continue;
                }

                state.TryGetValue(depId, out var depState);
                if (depState == 1)
                {
                    var cycleStart = stack.FindIndex(x => string.Equals(x, depId, StringComparison.OrdinalIgnoreCase));
                    var cycleNodes = cycleStart >= 0 ? stack.Skip(cycleStart).Append(depId) : [currentId, depId];
                    var cycleText = string.Join(" -> ", cycleNodes);

                    addFinding(
                        RuleSeverity.Error,
                        ToJsonPointer(rule.Path),
                        ConsistencyAnalyserMessages.CircularDependencyCycle(cycleText),
                        currentId,
                        new Dictionary<string, object?> { ["check"] = "dependency_cycle", ["cycle"] = cycleText });
                }
                else if (depState == 0)
                {
                    DfsCycleCheck(depId, ruleMap, state, stack, addFinding);
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        state[currentId] = 2;
    }
}
