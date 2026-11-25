using EstateDataCollection.Common.Results;
using System.Text.Json.Nodes;

namespace EstateDataCollection.Application.Validators;

public sealed partial class ContractConsistencyAnalyzer
{
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
}
