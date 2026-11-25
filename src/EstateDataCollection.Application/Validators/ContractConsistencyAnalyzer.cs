using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators;

/// <summary>
/// Cross-checks JSON Schema AST against Quality Rules (JsonLogic, CEL, Unique) for:
/// path &amp; casing mismatches, enum/literal mismatches, runbook dependency graph errors,
/// severity/threshold inversions, and required-vs-conditional-optional conflicts.
/// </summary>
public sealed partial class ContractConsistencyAnalyzer : IContractConsistencyAnalyzer
{
    private const int MaxAstDepth = 12;
    private const string EngineIdValue = "contract-consistency-analyzer";
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

    public static ValidationEngineDescriptor EngineDescriptor { get; } = new(
        EngineId: EngineIdValue,
        DisplayName: "Contract Consistency Analyzer",
        Kind: ValidationEngineKinds.ContractConsistency,
        Description: "Static analyzer checking rule-to-schema paths, field casing, enum literals, DependsOn DAG/stage integrity, severity threshold inversions, and required-vs-optional conflicts.");

    public ValidationEngineDescriptor Descriptor
    {
        get { return EngineDescriptor; }
    }

    public ContractConsistencyReport Analyze(
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
                engineType: typeof(ContractConsistencyAnalyzer),
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
        AnalyzeRunbookGraph(rules, AddFinding);

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
                        $"Rule '{ruleId}' path '{rule.Path}' does not match JSON Schema casing. Did you mean '{pathRes.SuggestedPath}'?",
                        ruleId,
                        new Dictionary<string, object?> { ["check"] = "path_casing", ["suggestion"] = pathRes.SuggestedPath });
                    continue;
                }
                else if (pathRes.Status == AstLookupStatus.Missing)
                {
                    var hint = pathRes.SuggestedPath is not null
                        ? $" Did you mean '{pathRes.SuggestedPath}'?"
                        : string.Empty;
                    AddFinding(
                        RuleSeverity.Error,
                        ToJsonPointer(normalizedTargetPath),
                        $"Rule '{ruleId}' target path '{rule.Path}' does not exist in JSON Schema.{hint}",
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
            AnalyzeRequiredVsOptionalConflicts(
                rootAst,
                unconditionalRequiredRules,
                conditionalOptionalRules,
                AddFinding);
        }

        return new ContractConsistencyReport(findings, errors, warnings);
    }

    #region 1. Runbook Graph Analysis

    private static void AnalyzeRunbookGraph(
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
                        $"Rule '{ruleId}' dependsOn '{depId}', which does not exist in the quality rules contract.",
                        ruleId,
                        new Dictionary<string, object?> { ["check"] = "missing_dependency", ["dependsOn"] = depId });
                    continue;
                }

                if (depRule.Stage > rule.Stage)
                {
                    addFinding(
                        RuleSeverity.Error,
                        ToJsonPointer(rule.Path),
                        $"Rule '{ruleId}' (Stage {rule.Stage}) cannot depend on '{depId}' in a later Stage ({depRule.Stage}).",
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
                        $"Circular dependency cycle detected in 'dependsOn': {cycleText}.",
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

    #endregion

    #region 2. JsonLogic Analysis

    private static void AnalyzeJsonLogicRule(
        BusinessRule rule,
        string ruleId,
        string normalizedTargetPath,
        SchemaAstNode? rootAst,
        SchemaAstNode? targetAst,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding,
        List<NumericBoundRecord> numericBounds,
        Dictionary<string, string> unconditionalRequiredRules,
        List<(string CanonicalFieldKey, string FieldName, string RuleId, string PropertyPath)> conditionalOptionalRules)
    {
        var assertion = rule.EffectiveAssertion;
        if (assertion is null)
        {
            return;
        }

        var localScopes = new Stack<string>();
        localScopes.Push(normalizedTargetPath);

        TraverseJsonLogicNode(
            assertion,
            rule,
            ruleId,
            normalizedTargetPath,
            localScopes,
            inIterationReduce: false,
            isTopLevel: true,
            rootAst,
            targetAst,
            addFinding,
            numericBounds,
            unconditionalRequiredRules,
            conditionalOptionalRules);
    }

    private static void TraverseJsonLogicNode(
        JsonNode? node,
        BusinessRule rule,
        string ruleId,
        string baseRulePath,
        Stack<string> scopeStack,
        bool inIterationReduce,
        bool isTopLevel,
        SchemaAstNode? rootAst,
        SchemaAstNode? targetAst,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding,
        List<NumericBoundRecord> numericBounds,
        Dictionary<string, string> unconditionalRequiredRules,
        List<(string CanonicalFieldKey, string FieldName, string RuleId, string PropertyPath)> conditionalOptionalRules)
    {
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                TraverseJsonLogicNode(item, rule, ruleId, baseRulePath, scopeStack, inIterationReduce, false, rootAst, targetAst, addFinding, numericBounds, unconditionalRequiredRules, conditionalOptionalRules);
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        var currentScope = scopeStack.Peek();

        // Check {"var": ...}
        if (obj.TryGetPropertyValue("var", out var varNode))
        {
            if (rootAst is not null && !inIterationReduce)
            {
                var varPath = ExtractJsonLogicVarPath(varNode);
                if (!string.IsNullOrWhiteSpace(varPath) && !IsNumericIndex(varPath) && !IsSpecialReference(varPath))
                {
                    var allowRootFallback = !varPath.StartsWith("$parent", StringComparison.Ordinal) &&
                                            !varPath.StartsWith("$root", StringComparison.Ordinal);
                    var lookup = ResolveExpressionReference(rootAst, currentScope, varPath, allowRootFallback);
                    ReportReferenceFinding(ruleId, varPath, lookup, addFinding);
                }
            }

            return;
        }

        // Collection operators: {"some"|"all"|"none"|"map"|"filter": [arrayExpr, predicateExpr]}
        foreach (var iterOp in new[] { "some", "all", "none", "map", "filter" })
        {
            if (obj.TryGetPropertyValue(iterOp, out var iterArgsNode) && iterArgsNode is JsonArray iterArgs && iterArgs.Count >= 2)
            {
                TraverseJsonLogicNode(iterArgs[0], rule, ruleId, baseRulePath, scopeStack, inIterationReduce, false, rootAst, targetAst, addFinding, numericBounds, unconditionalRequiredRules, conditionalOptionalRules);

                var pushedScope = false;
                if (iterArgs[0] is JsonObject arrObj && arrObj.TryGetPropertyValue("var", out var arrVarNode))
                {
                    var arrVar = ExtractJsonLogicVarPath(arrVarNode);
                    if (!string.IsNullOrWhiteSpace(arrVar) && !IsSpecialReference(arrVar))
                    {
                        var childScope = CombineScope(currentScope, arrVar);
                        scopeStack.Push(childScope);
                        pushedScope = true;
                    }
                }

                TraverseJsonLogicNode(iterArgs[1], rule, ruleId, baseRulePath, scopeStack, inIterationReduce, false, rootAst, targetAst, addFinding, numericBounds, unconditionalRequiredRules, conditionalOptionalRules);

                if (pushedScope)
                {
                    scopeStack.Pop();
                }

                return;
            }
        }

        if (obj.TryGetPropertyValue("reduce", out var reduceNode) && reduceNode is JsonArray reduceArgs && reduceArgs.Count >= 2)
        {
            TraverseJsonLogicNode(reduceArgs[0], rule, ruleId, baseRulePath, scopeStack, inIterationReduce, false, rootAst, targetAst, addFinding, numericBounds, unconditionalRequiredRules, conditionalOptionalRules);
            TraverseJsonLogicNode(reduceArgs[1], rule, ruleId, baseRulePath, scopeStack, inIterationReduce: true, false, rootAst, targetAst, addFinding, numericBounds, unconditionalRequiredRules, conditionalOptionalRules);
            if (reduceArgs.Count >= 3)
            {
                TraverseJsonLogicNode(reduceArgs[2], rule, ruleId, baseRulePath, scopeStack, inIterationReduce, false, rootAst, targetAst, addFinding, numericBounds, unconditionalRequiredRules, conditionalOptionalRules);
            }

            return;
        }

        // Enum / Literal comparisons: ==, ===, !=, !==, in
        if (rootAst is not null && !inIterationReduce)
        {
            CheckJsonLogicEnumComparison(obj, ruleId, currentScope, rootAst, addFinding);
        }

        // Numeric bounds extraction for threshold inversion analysis
        if (!inIterationReduce && isTopLevel)
        {
            ExtractJsonLogicNumericBound(obj, rule, ruleId, currentScope, rootAst, numericBounds);
            CheckJsonLogicRequiredPattern(obj, ruleId, currentScope, rootAst, unconditionalRequiredRules, conditionalOptionalRules);
        }

        foreach (var (_, value) in obj)
        {
            TraverseJsonLogicNode(value, rule, ruleId, baseRulePath, scopeStack, inIterationReduce, false, rootAst, targetAst, addFinding, numericBounds, unconditionalRequiredRules, conditionalOptionalRules);
        }
    }

    private static void CheckJsonLogicEnumComparison(
        JsonObject obj,
        string ruleId,
        string currentScope,
        SchemaAstNode rootAst,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding)
    {
        foreach (var op in new[] { "==", "===", "!=", "!==" })
        {
            if (obj.TryGetPropertyValue(op, out var argsNode) && argsNode is JsonArray args && args.Count == 2)
            {
                TryValidateVarAgainstLiteral(args[0], args[1], ruleId, currentScope, rootAst, addFinding);
                TryValidateVarAgainstLiteral(args[1], args[0], ruleId, currentScope, rootAst, addFinding);
            }
        }

        if (obj.TryGetPropertyValue("in", out var inArgsNode) && inArgsNode is JsonArray inArgs && inArgs.Count == 2 &&
            inArgs[1] is JsonArray allowedList)
        {
            var varPath = TryGetJsonLogicVar(inArgs[0]);
            if (varPath is not null)
            {
                foreach (var item in allowedList)
                {
                    if (item is JsonValue litVal && litVal.TryGetValue<string>(out var literalStr))
                    {
                        ValidateEnumLiteral(varPath, literalStr, ruleId, currentScope, rootAst, addFinding);
                    }
                }
            }
        }
    }

    private static void TryValidateVarAgainstLiteral(
        JsonNode? varCandidate,
        JsonNode? literalCandidate,
        string ruleId,
        string currentScope,
        SchemaAstNode rootAst,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding)
    {
        var varPath = TryGetJsonLogicVar(varCandidate);
        if (varPath is null || literalCandidate is not JsonValue litVal || !litVal.TryGetValue<string>(out var literalStr))
        {
            return;
        }

        ValidateEnumLiteral(varPath, literalStr, ruleId, currentScope, rootAst, addFinding);
    }

    private static string? TryGetJsonLogicVar(JsonNode? node)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue("var", out var v))
        {
            var path = ExtractJsonLogicVarPath(v);
            if (!string.IsNullOrWhiteSpace(path) && !IsSpecialReference(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? ExtractJsonLogicVarPath(JsonNode? varNode)
    {
        if (varNode is JsonValue val && val.TryGetValue<string>(out var s))
        {
            return s.Trim();
        }

        if (varNode is JsonArray arr && arr.Count > 0 && arr[0] is JsonValue first && first.TryGetValue<string>(out var firstStr))
        {
            return firstStr.Trim();
        }

        return null;
    }

    private static void ExtractJsonLogicNumericBound(
        JsonObject obj,
        BusinessRule rule,
        string ruleId,
        string scope,
        SchemaAstNode? rootAst,
        List<NumericBoundRecord> numericBounds)
    {
        foreach (var op in new[] { "<", "<=", ">", ">=" })
        {
            if (!obj.TryGetPropertyValue(op, out var argsNode) || argsNode is not JsonArray args || args.Count != 2)
            {
                continue;
            }

            var leftVar = TryGetJsonLogicVar(args[0]);
            var rightVar = TryGetJsonLogicVar(args[1]);
            if (leftVar is not null && TryGetNumericLiteral(args[1], out var rightNum))
            {
                var canonicalField = CanonicalizeFieldKey(rootAst, scope, leftVar);
                numericBounds.Add(new NumericBoundRecord(
                    RuleId: ruleId,
                    Severity: rule.Severity,
                    CanonicalFieldKey: canonicalField,
                    FieldName: leftVar,
                    PropertyPath: ToJsonPointer(scope),
                    Direction: op is "<" or "<=" ? BoundDirection.Upper : BoundDirection.Lower,
                    IsInclusive: op is "<=" or ">=",
                    Threshold: rightNum));
            }
            else if (rightVar is not null && TryGetNumericLiteral(args[0], out var leftNum))
            {
                // Flipped: 100 >= field  ===  field <= 100
                var flippedDirection = op is ">" or ">=" ? BoundDirection.Upper : BoundDirection.Lower;
                var canonicalField = CanonicalizeFieldKey(rootAst, scope, rightVar);
                numericBounds.Add(new NumericBoundRecord(
                    RuleId: ruleId,
                    Severity: rule.Severity,
                    CanonicalFieldKey: canonicalField,
                    FieldName: rightVar,
                    PropertyPath: ToJsonPointer(scope),
                    Direction: flippedDirection,
                    IsInclusive: op is "<=" or ">=",
                    Threshold: leftNum));
            }
        }
    }

    private static void CheckJsonLogicRequiredPattern(
        JsonObject obj,
        string ruleId,
        string scope,
        SchemaAstNode? rootAst,
        Dictionary<string, string> unconditionalRequiredRules,
        List<(string CanonicalFieldKey, string FieldName, string RuleId, string PropertyPath)> conditionalOptionalRules)
    {
        // Unconditional required: {"!=": [{"var": "x"}, null]} or {"!!": {"var": "x"}}
        if (obj.TryGetPropertyValue("!=", out var neArgs) && neArgs is JsonArray neArr && neArr.Count == 2 &&
            neArr[1] is null && TryGetJsonLogicVar(neArr[0]) is { } reqVar)
        {
            unconditionalRequiredRules.TryAdd(CanonicalizeFieldKey(rootAst, scope, reqVar), ruleId);
        }

        // Conditional optionality: {"if": [cond, true/check, ...]}
        if (obj.TryGetPropertyValue("if", out var ifNode) && ifNode is JsonArray ifArr && ifArr.Count >= 3)
        {
            var thenNode = ifArr[1];
            var elseNode = ifArr[2];
            var exemptsField = (thenNode is JsonValue tv && tv.TryGetValue<bool>(out var tb) && tb) ||
                               (elseNode is JsonValue ev && ev.TryGetValue<bool>(out var eb) && eb);

            if (exemptsField)
            {
                var varsInIf = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectJsonLogicVars(ifArr[1], varsInIf);
                CollectJsonLogicVars(ifArr[2], varsInIf);
                foreach (var v in varsInIf)
                {
                    conditionalOptionalRules.Add((CanonicalizeFieldKey(rootAst, scope, v), v, ruleId, ToJsonPointer(scope)));
                }
            }
        }
    }

    private static void CollectJsonLogicVars(JsonNode? node, HashSet<string> sink)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("var", out var v) && ExtractJsonLogicVarPath(v) is { Length: > 0 } p && !IsSpecialReference(p))
            {
                sink.Add(p);
                return;
            }

            foreach (var (_, val) in obj)
            {
                CollectJsonLogicVars(val, sink);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                CollectJsonLogicVars(item, sink);
            }
        }
    }

    #endregion

    #region 3. CEL Analysis

    private static void AnalyzeCelRule(
        BusinessRule rule,
        string ruleId,
        string normalizedTargetPath,
        string rawExpr,
        SchemaAstNode? rootAst,
        SchemaAstNode? targetAst,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding,
        List<NumericBoundRecord> numericBounds,
        Dictionary<string, string> unconditionalRequiredRules,
        List<(string CanonicalFieldKey, string FieldName, string RuleId, string PropertyPath)> conditionalOptionalRules)
    {
        var masked = MaskStringLiterals(rawExpr, out var stringLiterals);

        // Collect comprehension loop variables so e.g. s.id inside sites.all(s, s.id != '') isn't flagged as missing root field 's'
        var loopVariables = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (Match compMatch in CelComprehensionRegex().Matches(masked))
        {
            var collectionExpr = compMatch.Groups["coll"].Value;
            var loopVar = compMatch.Groups["var"].Value;
            var normColl = NormalizeCelFieldForLookup(collectionExpr, normalizedTargetPath, out var collScope);
            if (normColl is not null)
            {
                var collParts = normColl.Split('.');
                if (collParts.Length > 1 && loopVariables.TryGetValue(collParts[0], out var outerLoopScope) && !string.IsNullOrWhiteSpace(outerLoopScope))
                {
                    collScope = outerLoopScope;
                    normColl = string.Join('.', collParts.Skip(1));
                }

                loopVariables[loopVar] = CombineScope(collScope, normColl);
            }
        }

        // Check identifier/dotted path references against Schema AST
        if (rootAst is not null)
        {
            foreach (Match refMatch in CelIdentifierChainRegex().Matches(masked))
            {
                var token = refMatch.Value;
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var effectiveToken = token;
                var afterIndex = refMatch.Index + refMatch.Length;
                var dotParts = effectiveToken.Split('.');

                // If followed by '(', skip bare function calls or strip known CEL method suffix to validate receiver path
                if (afterIndex < masked.Length && masked[afterIndex] == '(')
                {
                    if (dotParts.Length > 1 && CelReservedWords.Contains(dotParts[^1]))
                    {
                        dotParts = dotParts[..^1];
                        effectiveToken = string.Join('.', dotParts);
                    }
                    else
                    {
                        continue;
                    }
                }
                else if (dotParts.Length > 1 && CelReservedWords.Contains(dotParts[^1]))
                {
                    dotParts = dotParts[..^1];
                    effectiveToken = string.Join('.', dotParts);
                }

                var firstSeg = dotParts[0];

                if (CelReservedWords.Contains(firstSeg) && dotParts.Length == 1)
                {
                    continue;
                }

                if (IsSpecialReference(firstSeg))
                {
                    continue;
                }

                // Normalize this.X / self.X / item.X
                string lookupScope = normalizedTargetPath;
                string lookupExpr = effectiveToken;
                bool allowRootFallback = true;

                if (firstSeg is "this" or "self" or "item")
                {
                    if (dotParts.Length == 1)
                    {
                        continue;
                    }

                    lookupExpr = string.Join('.', dotParts.Skip(1));
                    allowRootFallback = false;
                }
                else if (firstSeg is "$root" or "root" or "__edc_root")
                {
                    if (dotParts.Length == 1)
                    {
                        continue;
                    }

                    lookupScope = string.Empty;
                    lookupExpr = string.Join('.', dotParts.Skip(1));
                    allowRootFallback = false;
                }
                else if (loopVariables.TryGetValue(firstSeg, out var loopScope))
                {
                    if (dotParts.Length == 1 || string.IsNullOrWhiteSpace(loopScope))
                    {
                        continue;
                    }

                    lookupScope = loopScope;
                    lookupExpr = string.Join('.', dotParts.Skip(1));
                    allowRootFallback = false;
                }
                else if (firstSeg is "$parent" or "__edc_parent")
                {
                    allowRootFallback = false;
                }


                var lookup = ResolveExpressionReference(rootAst, lookupScope, lookupExpr, allowRootFallback);
                ReportReferenceFinding(ruleId, effectiveToken, lookup, addFinding);
            }

            // Enum / Literal comparisons in CEL: field == __STR_0__ or __STR_0__ == field or field != __STR_0__ or field in [...]
            foreach (Match eqMatch in CelEnumEqRegex().Matches(masked))
            {
                var fieldRef = eqMatch.Groups["field1"].Success ? eqMatch.Groups["field1"].Value : eqMatch.Groups["field2"].Value;
                var strIdx = int.Parse(eqMatch.Groups["idx1"].Success ? eqMatch.Groups["idx1"].Value : eqMatch.Groups["idx2"].Value, CultureInfo.InvariantCulture);
                if (strIdx >= 0 && strIdx < stringLiterals.Count)
                {
                    var normalizedRef = NormalizeCelFieldForLookup(fieldRef, normalizedTargetPath, out var effectiveScope);
                    if (normalizedRef is not null && !IsSpecialReference(normalizedRef))
                    {
                        ValidateEnumLiteral(normalizedRef, stringLiterals[strIdx], ruleId, effectiveScope, rootAst, addFinding);
                    }
                }
            }

            foreach (Match inMatch in CelEnumInListRegex().Matches(masked))
            {
                var fieldRef = inMatch.Groups["field"].Value;
                var listBody = inMatch.Groups["items"].Value;
                var normalizedRef = NormalizeCelFieldForLookup(fieldRef, normalizedTargetPath, out var effectiveScope);
                if (normalizedRef is not null && !IsSpecialReference(normalizedRef))
                {
                    foreach (Match idxMatch in StringPlaceholderRegex().Matches(listBody))
                    {
                        var strIdx = int.Parse(idxMatch.Groups["idx"].Value, CultureInfo.InvariantCulture);
                        if (strIdx >= 0 && strIdx < stringLiterals.Count)
                        {
                            ValidateEnumLiteral(normalizedRef, stringLiterals[strIdx], ruleId, effectiveScope, rootAst, addFinding);
                        }
                    }
                }
            }
        }

        // Numeric bounds in standalone / top-level CEL comparisons
        foreach (Match numMatch in CelSimpleNumericBoundRegex().Matches(masked))
        {
            var fieldRef = numMatch.Groups["field"].Value;
            var op = numMatch.Groups["op"].Value;
            if (!double.TryParse(numMatch.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numVal))
            {
                continue;
            }

            // Ignore ternary/conditional compound expressions like "flag == 'A' || amount <= 10" — still check unless obviously unrelated?
            // Wait: if the expression is purely or primarily comparing field with op num (or top-level &&):
            var normalizedRef = NormalizeCelFieldForLookup(fieldRef, normalizedTargetPath, out var effectiveScope);
            if (normalizedRef is null || IsSpecialReference(normalizedRef))
            {
                continue;
            }

            // If inside an implication guard ("cond ? expr : true" or "!cond || expr"), skip bound inversion check to avoid conditional branches colliding cleanly,
            // EXCEPT when comparing two unconditional or matching rules cleanly:
            if (masked.Contains('?', StringComparison.Ordinal) || Regex.IsMatch(masked, @"\|\|"))
            {
                // Still inspect for conditional-optional exemptions!
                continue;
            }

            numericBounds.Add(new NumericBoundRecord(
                RuleId: ruleId,
                Severity: rule.Severity,
                CanonicalFieldKey: CanonicalizeFieldKey(rootAst, effectiveScope, normalizedRef),
                FieldName: normalizedRef,
                PropertyPath: ToJsonPointer(effectiveScope),
                Direction: op is "<" or "<=" ? BoundDirection.Upper : BoundDirection.Lower,
                IsInclusive: op is "<=" or ">=",
                Threshold: numVal));
        }

        // Required vs Conditional-Optional CEL patterns
        var hasConditionalGuard = masked.Contains("||", StringComparison.Ordinal) || masked.Contains('?', StringComparison.Ordinal);
        if (!hasConditionalGuard)
        {
            foreach (Match reqMatch in CelUnconditionalNotNullRegex().Matches(masked))
            {
                var fieldRef = reqMatch.Groups["field"].Value;
                var normalizedRef = NormalizeCelFieldForLookup(fieldRef, normalizedTargetPath, out var effectiveScope);
                if (normalizedRef is not null && !IsSpecialReference(normalizedRef))
                {
                    unconditionalRequiredRules.TryAdd(CanonicalizeFieldKey(rootAst, effectiveScope, normalizedRef), ruleId);
                }
            }
        }
        else
        {
            // Conditional exemption mentioning !has(this.F), has(this.F), or this.F == null alongside an implication ("||" / "?")
            foreach (Match optMatch in CelConditionalOptionalFieldRegex().Matches(masked))
            {
                var fieldRef = optMatch.Groups["field"].Value;
                var normalizedRef = NormalizeCelFieldForLookup(fieldRef, normalizedTargetPath, out var effectiveScope);
                if (normalizedRef is not null && !IsSpecialReference(normalizedRef))
                {
                    conditionalOptionalRules.Add((
                        CanonicalizeFieldKey(rootAst, effectiveScope, normalizedRef),
                        normalizedRef,
                        ruleId,
                        ToJsonPointer(effectiveScope)));
                }
            }
        }
    }

    private static string? NormalizeCelFieldForLookup(string rawRef, string currentScope, out string effectiveScope)
    {
        effectiveScope = currentScope;
        if (string.IsNullOrWhiteSpace(rawRef))
        {
            return null;
        }

        var parts = rawRef.Trim().Split('.');
        if (parts[0] is "this" or "self" or "item")
        {
            return parts.Length > 1 ? string.Join('.', parts.Skip(1)) : null;
        }

        if (parts[0] is "$root" or "root" or "__edc_root")
        {
            effectiveScope = string.Empty;
            return parts.Length > 1 ? string.Join('.', parts.Skip(1)) : null;
        }

        return rawRef.Trim();
    }

    private static string MaskStringLiterals(string input, out List<string> literals)
    {
        literals = [];
        var sb = new StringBuilder(input.Length);
        var i = 0;

        while (i < input.Length)
        {
            var ch = input[i];
            if (ch is '\'' or '"')
            {
                var quote = ch;
                var litSb = new StringBuilder();
                i++;
                while (i < input.Length)
                {
                    var c = input[i];
                    if (c == '\\' && i + 1 < input.Length)
                    {
                        litSb.Append(input[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (c == quote)
                    {
                        i++;
                        break;
                    }

                    litSb.Append(c);
                    i++;
                }

                var idx = literals.Count;
                literals.Add(litSb.ToString());
                sb.Append($"__STR_{idx}__");
                continue;
            }

            sb.Append(ch);
            i++;
        }

        return sb.ToString();
    }

    #endregion

    #region 4. Enum, Threshold Inversion & Required-vs-Optional Checks

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
        var hint = caseMatch is not null
            ? $" Did you mean '{caseMatch}'?"
            : string.Empty;

        addFinding(
            RuleSeverity.Error,
            ToJsonPointer(res.CanonicalPath ?? scope),
            $"Rule '{ruleId}' compares field '{varPath}' against enum literal '{literalValue}', which is not in the JSON Schema enum [{string.Join(", ", enumVals.Select(v => $"'{v}'"))}].{hint}",
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
        // 4a: Pairwise severity vs bound tightness inversions on same field + direction
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
                        addFinding(
                            RuleSeverity.Warning,
                            lowerSeverity.PropertyPath,
                            $"Threshold/severity inversion on '{lowerSeverity.FieldName}': {higherSeverity.Severity} rule '{higherSeverity.RuleId}' enforces a stricter {(group.Key.Direction == BoundDirection.Upper ? "maximum" : "minimum")} ({higherSeverity.Threshold}) than {lowerSeverity.Severity} rule '{lowerSeverity.RuleId}' ({lowerSeverity.Threshold}), making the {lowerSeverity.Severity} rule unreachable.",
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

        // 4b: Rule bound contradicting JSON Schema minimum/maximum
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
                    $"Rule '{bound.RuleId}' requires '{bound.FieldName}' >= {bound.Threshold}, which conflicts with JSON Schema maximum ({schemaMax}).",
                    bound.RuleId,
                    new Dictionary<string, object?> { ["check"] = "schema_bound_conflict" });
            }
            else if (bound.Direction == BoundDirection.Upper && lookup.Node.Minimum is { } schemaMin && bound.Threshold < schemaMin)
            {
                addFinding(
                    RuleSeverity.Warning,
                    bound.PropertyPath,
                    $"Rule '{bound.RuleId}' requires '{bound.FieldName}' <= {bound.Threshold}, which conflicts with JSON Schema minimum ({schemaMin}).",
                    bound.RuleId,
                    new Dictionary<string, object?> { ["check"] = "schema_bound_conflict" });
            }
        }
    }

    private static void AnalyzeRequiredVsOptionalConflicts(
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
                    $"Field '{fieldName}' is unconditionally marked 'required' in JSON Schema, conflicting with conditional-optional exemption in rule '{ruleId}'.",
                    ruleId,
                    new Dictionary<string, object?> { ["check"] = "required_vs_conditional_optional", ["field"] = fieldName });
            }
            else if (unconditionalRequiredRules.TryGetValue(canonicalKey, out var reqRuleId) &&
                     !string.Equals(reqRuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            {
                addFinding(
                    RuleSeverity.Warning,
                    propPath,
                    $"Field '{fieldName}' is unconditionally required by rule '{reqRuleId}', conflicting with conditional-optional exemption in rule '{ruleId}'.",
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

    #endregion

    #region Schema AST Builder & Resolution

    private static SchemaAstNode? TryBuildSchemaAst(string rawSchemaJson)
    {
        try
        {
            if (JsonNode.Parse(rawSchemaJson) is not JsonObject rootObj)
            {
                return null;
            }

            return BuildNode(rootObj, rootObj, canonicalPath: string.Empty, isRequired: false, depth: 0, []);
        }
        catch
        {
            return null;
        }
    }

    private static SchemaAstNode BuildNode(
        JsonObject schemaObj,
        JsonObject rootSchema,
        string canonicalPath,
        bool isRequired,
        int depth,
        HashSet<string> visitedRefs)
    {
        var merged = ResolveEffectiveSchemaObjects(schemaObj, rootSchema, visitedRefs);
        var requiredSet = new HashSet<string>(StringComparer.Ordinal);
        var enumValues = new List<string>();
        double? minNum = null;
        double? maxNum = null;

        foreach (var obj in merged)
        {
            if (obj.TryGetPropertyValue("required", out var reqNode) && reqNode is JsonArray reqArr)
            {
                foreach (var item in reqArr)
                {
                    if (item is JsonValue v && v.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
                    {
                        requiredSet.Add(name);
                    }
                }
            }

            if (obj.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArr)
            {
                foreach (var item in enumArr)
                {
                    if (item is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        enumValues.Add(s);
                    }
                }
            }
            else if (obj.TryGetPropertyValue("const", out var constNode) && constNode is JsonValue cv && cv.TryGetValue<string>(out var cs))
            {
                enumValues.Add(cs);
            }

            if (obj.TryGetPropertyValue("minimum", out var minNode) && TryGetNumericLiteral(minNode, out var mn))
            {
                minNum = mn;
            }

            if (obj.TryGetPropertyValue("maximum", out var maxNode) && TryGetNumericLiteral(maxNode, out var mx))
            {
                maxNum = mx;
            }
        }

        var properties = new Dictionary<string, SchemaAstNode>(StringComparer.Ordinal);
        SchemaAstNode? itemsNode = null;
        var hasExplicitProperties = false;

        if (depth < MaxAstDepth)
        {
            foreach (var obj in merged)
            {
                if (obj.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject propsObj)
                {
                    hasExplicitProperties = true;
                    foreach (var (propName, propVal) in propsObj)
                    {
                        if (propVal is JsonObject propSchemaObj && !properties.ContainsKey(propName))
                        {
                            var childPath = string.IsNullOrEmpty(canonicalPath) ? propName : $"{canonicalPath}.{propName}";
                            properties[propName] = BuildNode(
                                propSchemaObj,
                                rootSchema,
                                childPath,
                                requiredSet.Contains(propName),
                                depth + 1,
                                new HashSet<string>(visitedRefs, StringComparer.OrdinalIgnoreCase));
                        }
                    }
                }

                if (itemsNode is null && obj.TryGetPropertyValue("items", out var itemsVal) && itemsVal is JsonObject itemsObj)
                {
                    var itemPath = string.IsNullOrEmpty(canonicalPath) ? "[]" : $"{canonicalPath}[]";
                    itemsNode = BuildNode(
                        itemsObj,
                        rootSchema,
                        itemPath,
                        isRequired: false,
                        depth + 1,
                        new HashSet<string>(visitedRefs, StringComparer.OrdinalIgnoreCase));
                }
            }
        }

        return new SchemaAstNode(
            CanonicalPath: canonicalPath,
            Properties: properties,
            HasExplicitProperties: hasExplicitProperties,
            Items: itemsNode,
            IsRequiredInParent: isRequired,
            EnumValues: enumValues.Count > 0 ? enumValues.Distinct(StringComparer.Ordinal).ToList() : null,
            Minimum: minNum,
            Maximum: maxNum);
    }

    private static List<JsonObject> ResolveEffectiveSchemaObjects(
        JsonObject schemaObj,
        JsonObject rootSchema,
        HashSet<string> visitedRefs)
    {
        var list = new List<JsonObject> { schemaObj };

        if (schemaObj.TryGetPropertyValue("$ref", out var refNode) &&
            refNode is JsonValue refVal &&
            refVal.TryGetValue<string>(out var refPointer) &&
            !string.IsNullOrWhiteSpace(refPointer) &&
            visitedRefs.Add(refPointer))
        {
            var resolved = ResolveLocalPointer(rootSchema, refPointer);
            if (resolved is not null)
            {
                list.AddRange(ResolveEffectiveSchemaObjects(resolved, rootSchema, visitedRefs));
            }
        }

        foreach (var combinator in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (schemaObj.TryGetPropertyValue(combinator, out var combNode) && combNode is JsonArray combArr)
            {
                foreach (var branch in combArr)
                {
                    if (branch is JsonObject branchObj)
                    {
                        list.AddRange(ResolveEffectiveSchemaObjects(branchObj, rootSchema, visitedRefs));
                    }
                }
            }
        }

        return list;
    }

    private static JsonObject? ResolveLocalPointer(JsonObject root, string pointer)
    {
        if (!pointer.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = pointer[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = root;
        foreach (var rawPart in parts)
        {
            var unescaped = rawPart.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current is JsonObject obj && obj.TryGetPropertyValue(unescaped, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }

        return current as JsonObject;
    }

    private static AstLookupResult ResolveExpressionReference(
        SchemaAstNode rootAst,
        string currentScope,
        string exprPath,
        bool allowRootFallback)
    {
        var expandedPath = ExpandRelativeScope(currentScope, exprPath);
        var primary = ResolvePathInAst(rootAst, expandedPath);
        if (primary.Status == AstLookupStatus.Found || primary.Status == AstLookupStatus.OpenObject)
        {
            return primary;
        }

        if (allowRootFallback && !IsRootPath(currentScope))
        {
            var fromRoot = ResolvePathInAst(rootAst, exprPath);
            if (fromRoot.Status == AstLookupStatus.Found || fromRoot.Status == AstLookupStatus.OpenObject)
            {
                return fromRoot;
            }
        }

        return primary;
    }

    private static string ExpandRelativeScope(string currentScope, string exprPath)
    {
        var normalizedExpr = NormaliseRulePath(exprPath);
        if (normalizedExpr.StartsWith("$root.", StringComparison.Ordinal))
        {
            return normalizedExpr["$root.".Length..];
        }

        if (string.Equals(normalizedExpr, "$root", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var scopeSegments = SplitSegments(currentScope).ToList();
        var exprSegments = SplitSegments(normalizedExpr).ToList();

        while (exprSegments.Count > 0 && exprSegments[0] is "$parent" or "__edc_parent")
        {
            exprSegments.RemoveAt(0);
            if (scopeSegments.Count > 0)
            {
                scopeSegments.RemoveAt(scopeSegments.Count - 1);
            }
        }

        scopeSegments.AddRange(exprSegments);
        return string.Join('.', scopeSegments);
    }

    private static string CombineScope(string currentScope, string childExpr)
    {
        return ExpandRelativeScope(currentScope, childExpr);
    }

    private static string CanonicalizeFieldKey(SchemaAstNode? rootAst, string scope, string fieldExpr)
    {
        var expanded = ExpandRelativeScope(scope, fieldExpr);
        if (rootAst is not null)
        {
            var res = ResolveExpressionReference(rootAst, scope, fieldExpr, allowRootFallback: true);
            if (res.Status == AstLookupStatus.Found && !string.IsNullOrWhiteSpace(res.CanonicalPath))
            {
                return res.CanonicalPath.Replace("[]", string.Empty, StringComparison.Ordinal);
            }
        }

        return expanded.Replace("[]", string.Empty, StringComparison.Ordinal);
    }

    private static AstLookupResult ResolvePathInAst(SchemaAstNode rootAst, string rawPath)
    {
        var normalized = NormaliseRulePath(rawPath);
        if (IsRootPath(normalized))
        {
            return new AstLookupResult(AstLookupStatus.Found, rootAst, rootAst.CanonicalPath, null);
        }

        var segments = SplitSegments(normalized);
        var current = rootAst;
        var canonicalMatched = new List<string>();

        for (var i = 0; i < segments.Length; i++)
        {
            var rawSeg = segments[i];
            var hadArraySuffix = rawSeg.EndsWith("[]", StringComparison.Ordinal);
            var segName = hadArraySuffix ? rawSeg[..^2] : StripBracketIndex(rawSeg);
            var hadIndexSuffix = rawSeg.Length != segName.Length;

            if (string.IsNullOrEmpty(segName) && current.Items is not null)
            {
                current = current.Items;
                continue;
            }

            if (!current.HasExplicitProperties)
            {
                // Open object / untyped schema node — cannot prove field missing
                return new AstLookupResult(AstLookupStatus.OpenObject, null, null, null);
            }

            if (current.Properties.TryGetValue(segName, out var exactChild))
            {
                canonicalMatched.Add(exactChild.Items is not null && hadIndexSuffix ? $"{segName}[]" : segName);
                current = (hadIndexSuffix || (i + 1 < segments.Length && exactChild.Items is not null)) && exactChild.Items is not null
                    ? exactChild.Items
                    : exactChild;
                continue;
            }

            // Case-insensitive exact match
            var caseMatch = current.Properties.Keys.FirstOrDefault(k => string.Equals(k, segName, StringComparison.OrdinalIgnoreCase));
            if (caseMatch is not null)
            {
                canonicalMatched.Add(caseMatch);
                return new AstLookupResult(
                    AstLookupStatus.CaseMismatch,
                    null,
                    null,
                    string.Join('.', canonicalMatched));
            }

            // Fuzzy suggestion (Levenshtein distance <= 2 or 3)
            var fuzzy = FindClosestPropertyName(segName, current.Properties.Keys);
            string? suggestedFull = null;
            if (fuzzy is not null)
            {
                canonicalMatched.Add(fuzzy);
                suggestedFull = string.Join('.', canonicalMatched);
            }

            return new AstLookupResult(AstLookupStatus.Missing, null, null, suggestedFull);
        }

        return new AstLookupResult(AstLookupStatus.Found, current, current.CanonicalPath, null);
    }

    private static void ReportReferenceFinding(
        string ruleId,
        string exprToken,
        AstLookupResult lookup,
        Action<RuleSeverity, string, string, string?, IReadOnlyDictionary<string, object?>?> addFinding)
    {
        if (lookup.Status is AstLookupStatus.Found or AstLookupStatus.OpenObject)
        {
            return;
        }

        if (lookup.Status == AstLookupStatus.CaseMismatch)
        {
            addFinding(
                RuleSeverity.Error,
                ToJsonPointer(exprToken),
                $"Rule '{ruleId}' references '{exprToken}', which has mismatched casing against JSON Schema. Did you mean '{lookup.SuggestedPath}'?",
                ruleId,
                new Dictionary<string, object?> { ["check"] = "field_casing", ["suggestion"] = lookup.SuggestedPath });
        }
        else if (lookup.Status == AstLookupStatus.Missing)
        {
            var hint = lookup.SuggestedPath is not null
                ? $" Did you mean '{lookup.SuggestedPath}'?"
                : string.Empty;
            addFinding(
                RuleSeverity.Error,
                ToJsonPointer(exprToken),
                $"Rule '{ruleId}' references field '{exprToken}', which does not exist in JSON Schema.{hint}",
                ruleId,
                new Dictionary<string, object?> { ["check"] = "missing_field", ["suggestion"] = lookup.SuggestedPath });
        }
    }

    #endregion

    #region Helpers & Regex

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

    private static string? ExtractCelExpression(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
        {
            return s.Trim();
        }

        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("expression", out var e) && e is JsonValue ev && ev.TryGetValue<string>(out var es))
            {
                return es.Trim();
            }

            if (obj.TryGetPropertyValue("cel", out var c) && c is JsonValue cv && cv.TryGetValue<string>(out var cs))
            {
                return cs.Trim();
            }
        }

        return null;
    }

    private static string? FindClosestPropertyName(string candidate, IEnumerable<string> options)
    {
        string? best = null;
        var bestDist = int.MaxValue;
        var maxAllowed = candidate.Length <= 4 ? 1 : 3;

        foreach (var opt in options)
        {
            var dist = LevenshteinDistance(candidate.ToLowerInvariant(), opt.ToLowerInvariant());
            if (dist <= maxAllowed && dist < bestDist)
            {
                bestDist = dist;
                best = opt;
            }
        }

        return best;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }

    [GeneratedRegex(@"(?<coll>[\$A-Za-z_][\$A-Za-z0-9_.]*)\.(?:all|exists|exists_one|filter|map)\(\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*,", RegexOptions.CultureInvariant)]
    private static partial Regex CelComprehensionRegex();

    [GeneratedRegex(@"(?<![\w.$])[\$A-Za-z_][\$A-Za-z0-9_]*(?:\.[\$A-Za-z_][\$A-Za-z0-9_]*)*", RegexOptions.CultureInvariant)]
    private static partial Regex CelIdentifierChainRegex();

    [GeneratedRegex(@"(?:(?<field1>[\$A-Za-z_][\$A-Za-z0-9_.]*)\s*(?:==|!=)\s*__STR_(?<idx1>\d+)__|__STR_(?<idx2>\d+)__\s*(?:==|!=)\s*(?<field2>[\$A-Za-z_][\$A-Za-z0-9_.]*))", RegexOptions.CultureInvariant)]
    private static partial Regex CelEnumEqRegex();

    [GeneratedRegex(@"(?<field>[\$A-Za-z_][\$A-Za-z0-9_.]*)\s+in\s+\[(?<items>[^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex CelEnumInListRegex();

    [GeneratedRegex(@"__STR_(?<idx>\d+)__", RegexOptions.CultureInvariant)]
    private static partial Regex StringPlaceholderRegex();

    [GeneratedRegex(@"(?<field>[\$A-Za-z_][\$A-Za-z0-9_.]*)\s*(?<op><=|>=|<|>)\s*(?<num>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex CelSimpleNumericBoundRegex();

    [GeneratedRegex(@"(?<field>[\$A-Za-z_][\$A-Za-z0-9_.]*)\s*!=\s*null", RegexOptions.CultureInvariant)]
    private static partial Regex CelUnconditionalNotNullRegex();

    [GeneratedRegex(@"(?:has\(\s*(?<field>[\$A-Za-z_][\$A-Za-z0-9_.]*)\s*\)|(?<field>[\$A-Za-z_][\$A-Za-z0-9_.]*)\s*==\s*null)", RegexOptions.CultureInvariant)]
    private static partial Regex CelConditionalOptionalFieldRegex();

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

    #endregion
}
