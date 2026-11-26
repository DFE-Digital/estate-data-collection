using System.Globalization;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators;

/// <summary>
/// Cross-checks JSON Schema AST against Quality Rules (JsonLogic, CEL, Unique) for:
/// path &amp; casing mismatches, enum/literal mismatches, runbook dependency graph errors,
/// severity/threshold inversions, and required-vs-conditional-optional conflicts.
/// </summary>
public sealed partial class ContractConsistencyAnalyser : IContractConsistencyAnalyser
{
    private const int MaxAstDepth = 12;
    private const string EngineIdValue = "contract-consistency-analyser";
    private const string SourceTag = "Consistency";

    private static readonly HashSet<string> CelReservedWords = new(StringComparer.Ordinal)
    {
        "true", "false", "null", "in", "and", "or",
        "this", "self", "item", "root", "ambient", "answers",
        "has", "size", "int", "uint", "double", "string", "bool", "bytes",
        "timestamp", "duration", "type", "math", "dyn",
        "all", "exists", "exists_one", "filter", "map",
        "contains", "startsWith", "endsWith", "matches",
        "getFullYear", "getMonth", "getDate", "getDayOfMonth", "getDayOfWeek", "getDayOfYear",
        "getHours", "getMinutes", "getSeconds", "getMilliseconds"
    };

    private static readonly HashSet<string> SpecialAmbientRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "ambient", "$ancestors", "$index", "answers",
        "valid_site_ids", "valid_block_ids", "valid_evidence_ids", "valid_element_ids", "valid_product_types",
        "__edc_ancestors", "__edc_index"
    };

    public static WorkflowEngineDescriptor EngineDescriptor { get; } = new(
        EngineId: EngineIdValue,
        DisplayName: ConsistencyAnalyserMessages.DescriptorDisplayName,
        Kind: ValidationEngineKinds.ContractConsistency,
        Description: ConsistencyAnalyserMessages.DescriptorDescription);

    public WorkflowEngineDescriptor Descriptor => EngineDescriptor;

    public ContractConsistencyReport Analyse(
        string? rawSchemaJson,
        IReadOnlyList<BusinessRule>? rules)
    {
        if (rules is not { Count: > 0 })
        {
            return new ContractConsistencyReport([], [], []);
        }

        var findings = new List<ValidationError>();
        var errors = new List<ValidationMessage>();
        var warnings = new List<ValidationMessage>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFinding(
            RuleSeverity severity,
            string propertyPath,
            string message,
            string? ruleId,
            IReadOnlyDictionary<string, object?>? metadata = null)
        {
            var dedupKey = $"{severity}|{ruleId}|{propertyPath}|{message}";
            if (!seenKeys.Add(dedupKey))
            {
                return;
            }

            findings.Add(new ValidationError(
                PropertyPath: string.IsNullOrWhiteSpace(propertyPath) ? "/" : propertyPath,
                Message: message,
                RuleId: ruleId,
                Severity: severity,
                Action: severity.IsAtOrAbove(RuleSeverity.Error) ? "reject_schema" : "highlight_issue",
                EngineId: EngineIdValue));

            var vm = ValidationMessage.For(
                source: SourceTag,
                message: message,
                identifier: ruleId ?? propertyPath,
                path: string.IsNullOrWhiteSpace(propertyPath) ? "/" : propertyPath,
                ruleId: ruleId,
                engineType: typeof(ContractConsistencyAnalyser),
                metadata: metadata);

            if (severity.IsAtOrAbove(RuleSeverity.Error))
            {
                errors.Add(vm);
            }
            else
            {
                warnings.Add(vm);
            }
        }

        // 1. Runbook dependency graph checks (DAG, missing target, later stage)
        AnalyseRunbookGraph(rules, AddFinding);

        // 2. Parse JSON Schema AST if available
        SchemaAstNode? rootAst = null;
        if (!string.IsNullOrWhiteSpace(rawSchemaJson))
        {
            rootAst = TryBuildSchemaAst(rawSchemaJson);
        }

        // Collectors for cross-rule checks (threshold inversions + required vs conditional-optional)
        var numericBounds = new List<NumericBoundRecord>();
        var unconditionalRequiredRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conditionalOptionalRules = new List<(string CanonicalFieldKey, string FieldName, string RuleId, string PropertyPath)>();

        foreach (var rule in rules)
        {
            var ruleId = string.IsNullOrWhiteSpace(rule.Id) ? "rule" : rule.Id.Trim();
            var normalizedTargetPath = NormaliseRulePath(rule.Path);

            // Check 2a: Target rule.Path resolves against Schema AST
            SchemaAstNode? targetAst = rootAst;
            if (rootAst is not null && !IsRootPath(normalizedTargetPath))
            {
                var pathRes = ResolvePathInAst(rootAst, normalizedTargetPath);
                if (pathRes.Status == AstLookupStatus.Found)
                {
                    targetAst = pathRes.Node;
                }
                else if (pathRes.Status == AstLookupStatus.CaseMismatch)
                {
                    AddFinding(
                        RuleSeverity.Error,
                        ToJsonPointer(normalizedTargetPath),
                        ConsistencyAnalyserMessages.RulePathCaseMismatch(ruleId, rule.Path, pathRes.SuggestedPath),
                        ruleId,
                        new Dictionary<string, object?> { ["check"] = "path_casing", ["suggestion"] = pathRes.SuggestedPath });
                    continue;
                }
                else if (pathRes.Status == AstLookupStatus.Missing)
                {
                    AddFinding(
                        RuleSeverity.Error,
                        ToJsonPointer(normalizedTargetPath),
                        ConsistencyAnalyserMessages.RuleTargetPathMissing(ruleId, rule.Path, pathRes.SuggestedPath),
                        ruleId,
                        new Dictionary<string, object?> { ["check"] = "missing_path", ["suggestion"] = pathRes.SuggestedPath });
                    continue;
                }
            }

            var engine = rule.ResolveEngine();

            // Check 2b: Unique composite keys
            if (string.Equals(engine, "unique", StringComparison.OrdinalIgnoreCase) && rule.Fields is { Count: > 0 })
            {
                if (rootAst is not null && targetAst is not null)
                {
                    foreach (var fieldExpr in rule.Fields)
                    {
                        if (string.IsNullOrWhiteSpace(fieldExpr) || IsSpecialReference(fieldExpr))
                        {
                            continue;
                        }

                        var fieldRes = ResolveExpressionReference(rootAst, normalizedTargetPath, fieldExpr.Trim(), allowRootFallback: false);
                        ReportReferenceFinding(ruleId, fieldExpr.Trim(), fieldRes, AddFinding);
                    }
                }

                continue;
            }

            // Check 2c: JsonLogic rules
            if (string.Equals(engine, "jsonlogic", StringComparison.OrdinalIgnoreCase) && rule.EffectiveAssertion is not null)
            {
                AnalyzeJsonLogicRule(
                    rule,
                    ruleId,
                    normalizedTargetPath,
                    rootAst,
                    targetAst,
                    AddFinding,
                    numericBounds,
                    unconditionalRequiredRules,
                    conditionalOptionalRules);
            }
            // Check 2d: CEL rules
            else if (string.Equals(engine, "cel", StringComparison.OrdinalIgnoreCase))
            {
                var expr = ExtractCelExpression(rule.EffectiveAssertion);
                if (!string.IsNullOrWhiteSpace(expr))
                {
                    AnalyzeCelRule(
                        rule,
                        ruleId,
                        normalizedTargetPath,
                        expr,
                        rootAst,
                        targetAst,
                        AddFinding,
                        numericBounds,
                        unconditionalRequiredRules,
                        conditionalOptionalRules);
                }
            }
        }

        // Check 3: Severity / numeric threshold inversions (Warning)
        AnalyzeNumericBoundInversions(numericBounds, rootAst, AddFinding);

        // Check 4: Required vs conditional-optional conflicts (Warning)
        if (rootAst is not null || unconditionalRequiredRules.Count > 0)
        {
            AnalyseRequiredVsOptionalConflicts(
                rootAst,
                unconditionalRequiredRules,
                conditionalOptionalRules,
                AddFinding);
        }

        return new ContractConsistencyReport(findings, errors, warnings);
    }

    private static string NormaliseRulePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Trim() is "/" or "$")
        {
            return string.Empty;
        }

        var trimmed = rawPath.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith('/'))
        {
            trimmed = trimmed.TrimStart('/').Replace('/', '.');
        }

        return trimmed;
    }

    private static bool IsRootPath(string normalized) => string.IsNullOrWhiteSpace(normalized);

    private static string[] SplitSegments(string normalizedPath) =>
        string.IsNullOrWhiteSpace(normalizedPath)
            ? []
            : normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);

    private static string StripBracketIndex(string seg)
    {
        var idx = seg.IndexOf('[', StringComparison.Ordinal);
        return idx >= 0 ? seg[..idx] : seg;
    }

    private static string ToJsonPointer(string? path)
    {
        var norm = NormaliseRulePath(path);
        return string.IsNullOrEmpty(norm) ? "/" : "/" + norm.Replace('.', '/');
    }

    private static bool IsNumericIndex(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private static bool IsSpecialReference(string pathOrSegment)
    {
        var first = pathOrSegment.Split('.')[0];
        return SpecialAmbientRoots.Contains(first) || first.StartsWith("__STR_", StringComparison.Ordinal);
    }

    private static bool TryGetNumericLiteral(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue val)
        {
            return false;
        }

        if (val.TryGetValue<double>(out var d))
        {
            value = d;
            return true;
        }

        if (val.TryGetValue<decimal>(out var dec))
        {
            value = (double)dec;
            return true;
        }

        if (val.TryGetValue<long>(out var l))
        {
            value = l;
            return true;
        }

        return false;
    }

    private enum AstLookupStatus
    {
        Found,
        CaseMismatch,
        Missing,
        OpenObject
    }

    private sealed record SchemaAstNode(
        string CanonicalPath,
        Dictionary<string, SchemaAstNode> Properties,
        bool HasExplicitProperties,
        SchemaAstNode? Items,
        bool IsRequiredInParent,
        List<string>? EnumValues,
        double? Minimum,
        double? Maximum);

    private sealed record AstLookupResult(
        AstLookupStatus Status,
        SchemaAstNode? Node,
        string? CanonicalPath,
        string? SuggestedPath);

    private enum BoundDirection
    {
        Upper,
        Lower
    }

    private sealed record NumericBoundRecord(
        string RuleId,
        RuleSeverity Severity,
        string CanonicalFieldKey,
        string FieldName,
        string PropertyPath,
        BoundDirection Direction,
        bool IsInclusive,
        double Threshold);
}
