using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators;

public sealed partial class ContractConsistencyAnalyser
{
    private static void ValidateEnumLiteral(
        string varPath,
        string literalValue,
        string ruleId,
        string scope,
        SchemaAstNode rootAst,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding)
    {
        var allowRootFallback = !varPath.StartsWith("$parent", StringComparison.Ordinal) &&
                                !varPath.StartsWith("$root", StringComparison.Ordinal);
        var res = ResolveExpressionReference(rootAst, scope, varPath, allowRootFallback);
        if (res.Status != AstLookupStatus.Found || res.Node?.EnumValues is not { Count: > 0 } enumVals)
        {
            return;
        }

        if (enumVals.Contains(literalValue, StringComparer.Ordinal))
        {
            return;
        }

        var caseMatch = enumVals.FirstOrDefault(e => string.Equals(e, literalValue, StringComparison.OrdinalIgnoreCase));
        var allowedValuesJoined = string.Join(", ", enumVals.Select(v => $"'{v}'"));

        addFinding(
            RuleSeverity.Error,
            ToJsonPointer(res.CanonicalPath ?? scope),
            ConsistencyAnalyserMessages.EnumLiteralMismatch(ruleId, varPath, literalValue, allowedValuesJoined, caseMatch),
            ruleId,
            new Dictionary<string, object?>
            {
                ["check"] = "enum_literal_mismatch",
                ["literal"] = literalValue,
                ["allowedValues"] = enumVals.ToArray()
            });
    }

    private static void AnalyzeNumericBoundInversions(
        List<NumericBoundRecord> bounds,
        SchemaAstNode? rootAst,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding)
    {
        // Pairwise severity vs bound tightness inversions on same field + direction
        foreach (var group in bounds.GroupBy(b => (b.CanonicalFieldKey, b.Direction)))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    if (string.Equals(a.RuleId, b.RuleId, StringComparison.OrdinalIgnoreCase) || a.Severity == b.Severity)
                    {
                        continue;
                    }

                    // Remember RuleSeverity enum order: Critical = 0, Error = 1, Warning = 2, Info = 3
                    var higherSeverity = a.Severity < b.Severity ? a : b;
                    var lowerSeverity = a.Severity < b.Severity ? b : a;

                    var isInverted = group.Key.Direction == BoundDirection.Upper
                        ? higherSeverity.Threshold < lowerSeverity.Threshold
                        : higherSeverity.Threshold > lowerSeverity.Threshold;

                    if (isInverted)
                    {
                        var directionWord = group.Key.Direction == BoundDirection.Upper ? "maximum" : "minimum";
                        addFinding(
                            RuleSeverity.Warning,
                            lowerSeverity.PropertyPath,
                            ConsistencyAnalyserMessages.ThresholdSeverityInversion(
                                lowerSeverity.FieldName,
                                higherSeverity.Severity,
                                higherSeverity.RuleId,
                                directionWord,
                                higherSeverity.Threshold,
                                lowerSeverity.Severity,
                                lowerSeverity.RuleId,
                                lowerSeverity.Threshold),
                            lowerSeverity.RuleId,
                            new Dictionary<string, object?>
                            {
                                ["check"] = "threshold_severity_inversion",
                                ["conflictingRuleId"] = higherSeverity.RuleId
                            });
                    }
                }
            }
        }

        // Rule bound contradicting JSON Schema minimum/maximum
        if (rootAst is null)
        {
            return;
        }

        foreach (var bound in bounds)
        {
            var lookup = ResolvePathInAst(rootAst, bound.CanonicalFieldKey);
            if (lookup.Status != AstLookupStatus.Found || lookup.Node is null)
            {
                continue;
            }

            if (bound.Direction == BoundDirection.Lower && lookup.Node.Maximum is { } schemaMax && bound.Threshold > schemaMax)
            {
                addFinding(
                    RuleSeverity.Warning,
                    bound.PropertyPath,
                    ConsistencyAnalyserMessages.SchemaMinimumConflict(bound.RuleId, bound.FieldName, bound.Threshold, schemaMax),
                    bound.RuleId,
                    new Dictionary<string, object?> { ["check"] = "schema_bound_conflict" });
            }
            else if (bound.Direction == BoundDirection.Upper && lookup.Node.Minimum is { } schemaMin && bound.Threshold < schemaMin)
            {
                addFinding(
                    RuleSeverity.Warning,
                    bound.PropertyPath,
                    ConsistencyAnalyserMessages.SchemaMaximumConflict(bound.RuleId, bound.FieldName, bound.Threshold, schemaMin),
                    bound.RuleId,
                    new Dictionary<string, object?> { ["check"] = "schema_bound_conflict" });
            }
        }
    }

    private static void AnalyseRequiredVsOptionalConflicts(
        SchemaAstNode? rootAst,
        Dictionary<string, string> unconditionalRequiredRules,
        List<(string CanonicalFieldKey, string FieldName, string RuleId, string PropertyPath)> conditionalOptionalRules,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding)
    {
        foreach (var (canonicalKey, fieldName, ruleId, propPath) in conditionalOptionalRules)
        {
            var requiredInSchema = false;
            if (rootAst is not null)
            {
                var lookup = ResolvePathInAst(rootAst, canonicalKey);
                if (lookup.Status == AstLookupStatus.Found && lookup.Node?.IsRequiredInParent == true)
                {
                    requiredInSchema = true;
                }
            }

            if (requiredInSchema)
            {
                addFinding(
                    RuleSeverity.Warning,
                    propPath,
                    ConsistencyAnalyserMessages.RequiredInSchemaVsConditionalOptional(fieldName, ruleId),
                    ruleId,
                    new Dictionary<string, object?> { ["check"] = "required_vs_conditional_optional", ["field"] = fieldName });
            }
            else if (unconditionalRequiredRules.TryGetValue(canonicalKey, out var reqRuleId) &&
                     !string.Equals(reqRuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            {
                addFinding(
                    RuleSeverity.Warning,
                    propPath,
                    ConsistencyAnalyserMessages.RequiredByRuleVsConditionalOptional(fieldName, reqRuleId, ruleId),
                    ruleId,
                    new Dictionary<string, object?>
                    {
                        ["check"] = "required_vs_conditional_optional",
                        ["field"] = fieldName,
                        ["conflictingRuleId"] = reqRuleId
                    });
            }
        }
    }
}
