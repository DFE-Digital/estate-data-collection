using EstateDataCollection.Common.Results;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EstateDataCollection.Application.Validators.Logic;

namespace EstateDataCollection.Application.Validators;

public sealed partial class ContractConsistencyAnalyser
{
    #region JsonLogic Analysis

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

    #region CEL Analysis

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

            var normalizedRef = NormalizeCelFieldForLookup(fieldRef, normalizedTargetPath, out var effectiveScope);
            if (normalizedRef is null || IsSpecialReference(normalizedRef))
            {
                continue;
            }

            // If inside an implication guard ("cond ? expr : true" or "!cond || expr"), skip bound inversion check
            if (masked.Contains('?', StringComparison.Ordinal) || Regex.IsMatch(masked, @"\|\|"))
            {
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

    #endregion
}
