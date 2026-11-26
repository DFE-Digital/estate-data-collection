using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Infrastructure.Workflow.Engines;

/// <summary>
/// Built-in O(N) composite uniqueness engine (<c>engine: "unique"</c> or <c>type: "unique"</c>).
/// Checks uniqueness across resolved target collections without O(N^2) CEL comprehensions,
/// supporting local paths as well as hierarchical ancestor fields (e.g. <c>$parent.productType</c>).
/// </summary>
public sealed class UniqueCombinationQualityEngine : IBatchQualityRuleEngine
{
    public static readonly WorkflowEngineDescriptor EngineDescriptor = new(
        EngineId: "unique",
        DisplayName: WorkflowMessages.UniqueDisplayName,
        Kind: ValidationEngineKinds.QualityRule,
        Description: WorkflowMessages.UniqueDescription);

    public string EngineName => EngineDescriptor.EngineId;

    public WorkflowEngineDescriptor Descriptor => EngineDescriptor;

    public IReadOnlyList<ValidationMessage> ValidateSyntax(BusinessRule rule, string ruleId)
    {
        var config = ExtractConfig(rule);
        if (config.Fields.Count == 0)
        {
            return
            [
                new ValidationMessage(
                    typeof(UniqueCombinationQualityEngine),
                    ruleId,
                    WorkflowMessages.UniqueRuleRequiresField,
                    source: ValidationSources.Rule,
                    ruleId: ruleId)
            ];
        }

        return [];
    }

    public Result<bool> Evaluate(BusinessRule rule, JsonNode enrichedContextNode, JsonNode rootNode)
    {
        var config = ExtractConfig(rule);
        if (config.Fields.Count == 0)
        {
            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed,
                WorkflowMessages.UniqueRuleMissingFields(rule.Id));
        }

        if (enrichedContextNode is JsonArray arr)
        {
            var targets = new List<ResolvedTargetItem>(arr.Count);
            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] is { } item)
                {
                    targets.Add(new ResolvedTargetItem($"/{i}", item));
                }
            }

            var batch = EvaluateBatch(rule, targets, rootNode);
            return batch.IsFailure ? batch.Error : batch.Value.Count == 0;
        }

        return true;
    }

    public Result<IReadOnlyList<BatchRuleViolation>> EvaluateBatch(
        BusinessRule rule,
        IReadOnlyList<ResolvedTargetItem> targets,
        JsonNode rootNode)
    {
        var config = ExtractConfig(rule);
        if (config.Fields.Count == 0)
        {
            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed,
                WorkflowMessages.UniqueRuleMissingFields(rule.Id));
        }

        if (targets.Count <= 1)
        {
            return Result<IReadOnlyList<BatchRuleViolation>>.Success([]);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var violations = new List<BatchRuleViolation>();
        var sb = new StringBuilder(64);

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!TryBuildCompositeKey(target, rootNode, config, sb, out var key))
            {
                continue;
            }

            if (!seen.Add(key))
            {
                violations.Add(new BatchRuleViolation(target.TargetPath));
            }
        }

        return violations;
    }

    private static bool TryBuildCompositeKey(
        ResolvedTargetItem target,
        JsonNode rootNode,
        UniqueConfig config,
        StringBuilder sb,
        out string compositeKey)
    {
        sb.Clear();
        var anyNonNull = false;
        var allNull = true;

        for (var i = 0; i < config.Fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\u001F'); // Unit separator
            }

            var resolved = ResolveFieldPath(target, rootNode, config.Fields[i]);
            if (resolved is null)
            {
                if (config.IgnoreNulls)
                {
                    compositeKey = string.Empty;
                    return false;
                }

                sb.Append("\0NULL");
            }
            else
            {
                anyNonNull = true;
                allNull = false;
                AppendCanonicalNodeValue(sb, resolved, config.CaseInsensitive);
            }
        }

        if (config.IgnoreAllNull && (!anyNonNull || allNull))
        {
            compositeKey = string.Empty;
            return false;
        }

        compositeKey = sb.ToString();
        return true;
    }

    private static JsonNode? ResolveFieldPath(
        ResolvedTargetItem target,
        JsonNode rootNode,
        string dotPath)
    {
        if (string.IsNullOrWhiteSpace(dotPath))
        {
            return null;
        }

        // Fast zero-clone resolution when Ancestors/ChildFocusNode are supplied directly by QualityRuleCoordinator
        if (dotPath == "$index")
        {
            return target.Index.HasValue ? JsonValue.Create(target.Index.Value) : null;
        }

        if (dotPath == "$root")
        {
            return rootNode;
        }

        if (dotPath.StartsWith("$root.", StringComparison.Ordinal))
        {
            return ResolveDotPath(rootNode, dotPath["$root.".Length..]);
        }

        if (dotPath == "$parent" || dotPath.StartsWith("$parent.", StringComparison.Ordinal))
        {
            if (target.Ancestors is { Count: > 0 } ancestors)
            {
                var remaining = dotPath;
                var ancestorIndex = ancestors.Count;
                while (remaining == "$parent" || remaining.StartsWith("$parent.", StringComparison.Ordinal))
                {
                    ancestorIndex--;
                    if (ancestorIndex < 0)
                    {
                        return null;
                    }

                    remaining = remaining.Length == "$parent".Length
                        ? string.Empty
                        : remaining["$parent.".Length..];
                }

                return string.IsNullOrEmpty(remaining)
                    ? ancestors[ancestorIndex]
                    : ResolveDotPath(ancestors[ancestorIndex], remaining);
            }

            return ResolveDotPath(target.EnrichedContextNode, dotPath);
        }

        if (dotPath.StartsWith("$ancestors.", StringComparison.Ordinal))
        {
            if (target.Ancestors is { Count: > 0 } ancestors)
            {
                var rest = dotPath["$ancestors.".Length..];
                var dotIdx = rest.IndexOf('.');
                var idxSegment = dotIdx < 0 ? rest : rest[..dotIdx];
                if (int.TryParse(idxSegment, NumberStyles.None, CultureInfo.InvariantCulture, out var requestedIdx))
                {
                    // Skip rootNode if it's at index 0 to match BuildAncestorsArray indexing
                    var offset = ReferenceEquals(ancestors[0], rootNode) ? 1 : 0;
                    var actualIdx = requestedIdx + offset;
                    if (actualIdx >= 0 && actualIdx < ancestors.Count)
                    {
                        return dotIdx < 0
                            ? ancestors[actualIdx]
                            : ResolveDotPath(ancestors[actualIdx], rest[(dotIdx + 1)..]);
                    }
                }
                return null;
            }

            return ResolveDotPath(target.EnrichedContextNode, dotPath);
        }

        if (target.AmbientContext is { Count: > 0 } ambient)
        {
            var firstDot = dotPath.IndexOf('.');
            var firstSeg = firstDot < 0 ? dotPath : dotPath[..firstDot];
            if (ambient.TryGetValue(firstSeg, out var ambientRoot))
            {
                return firstDot < 0
                    ? ambientRoot
                    : ResolveDotPath(ambientRoot, dotPath[(firstDot + 1)..]);
            }
        }

        var local = ResolveDotPath(target.EnrichedContextNode, dotPath);
        if (local is not null)
        {
            return local;
        }

        if (target.ChildFocusNode is not null && !ReferenceEquals(target.ChildFocusNode, target.EnrichedContextNode))
        {
            var childMatch = ResolveDotPath(target.ChildFocusNode, dotPath);
            if (childMatch is not null)
            {
                return childMatch;
            }
        }

        if (!ReferenceEquals(target.EnrichedContextNode, rootNode))
        {
            return ResolveDotPath(rootNode, dotPath);
        }

        return null;
    }

    private static void AppendCanonicalNodeValue(StringBuilder sb, JsonNode node, bool caseInsensitive)
    {
        if (node is JsonValue val)
        {
            if (val.TryGetValue<JsonElement>(out var elem))
            {
                switch (elem.ValueKind)
                {
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        sb.Append("\0NULL");
                        return;
                    case JsonValueKind.True:
                        sb.Append("B:true");
                        return;
                    case JsonValueKind.False:
                        sb.Append("B:false");
                        return;
                    case JsonValueKind.Number when elem.TryGetDecimal(out var dec):
                        sb.Append("N:").Append(dec.ToString(CultureInfo.InvariantCulture));
                        return;
                    case JsonValueKind.Number when elem.TryGetDouble(out var dbl):
                        sb.Append("N:").Append(dbl.ToString("R", CultureInfo.InvariantCulture));
                        return;
                    case JsonValueKind.String:
                        AppendString(sb, elem.GetString() ?? string.Empty, caseInsensitive);
                        return;
                    default:
                        sb.Append("J:").Append(elem.GetRawText());
                        return;
                }
            }

            if (val.TryGetValue<bool>(out var b))
            {
                sb.Append(b ? "B:true" : "B:false");
                return;
            }

            if (val.TryGetValue<decimal>(out var decVal))
            {
                sb.Append("N:").Append(decVal.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (val.TryGetValue<long>(out var longVal))
            {
                sb.Append("N:").Append(longVal.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (val.TryGetValue<double>(out var dblVal))
            {
                sb.Append("N:").Append(dblVal.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (val.TryGetValue<string>(out var strVal))
            {
                AppendString(sb, strVal, caseInsensitive);
                return;
            }
        }

        sb.Append("J:").Append(node.ToJsonString());
    }

    private static void AppendString(StringBuilder sb, string s, bool caseInsensitive)
    {
        sb.Append("S:");
        if (caseInsensitive)
        {
            sb.Append(s.ToUpperInvariant());
        }
        else
        {
            sb.Append(s);
        }
    }

    private static JsonNode? ResolveDotPath(JsonNode? root, string dotPath)
    {
        if (root is null || string.IsNullOrWhiteSpace(dotPath))
        {
            return null;
        }

        // Fast path for single-segment property access (no string[] allocation)
        if (!dotPath.Contains('.'))
        {
            if (root is JsonObject singleObj)
            {
                return singleObj.TryGetPropertyValue(dotPath, out var singleVal) ? singleVal : null;
            }

            if (root is JsonArray singleArr &&
                int.TryParse(dotPath, NumberStyles.None, CultureInfo.InvariantCulture, out var singleIdx) &&
                singleIdx >= 0 && singleIdx < singleArr.Count)
            {
                return singleArr[singleIdx];
            }

            return null;
        }

        var current = root;
        foreach (var segment in dotPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out current))
                {
                    return null;
                }
            }
            else if (current is JsonArray arr &&
                     int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var idx) &&
                     idx >= 0 && idx < arr.Count)
            {
                current = arr[idx];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static UniqueConfig ExtractConfig(BusinessRule rule)
    {
        var fields = new List<string>();
        var ignoreNulls = false;
        var ignoreAllNull = false;
        var caseInsensitive = false;

        if (rule.Fields is { Count: > 0 })
        {
            foreach (var f in rule.Fields)
            {
                if (!string.IsNullOrWhiteSpace(f))
                {
                    fields.Add(f.Trim());
                }
            }
        }

        var assertion = rule.EffectiveAssertion;
        if (assertion is JsonArray directArray)
        {
            if (fields.Count == 0)
            {
                ReadFieldsArray(directArray, fields);
            }
        }
        else if (assertion is JsonObject obj)
        {
            if (fields.Count == 0)
            {
                if (obj.TryGetPropertyValue("fields", out var fieldsNode) && fieldsNode is JsonArray fa)
                {
                    ReadFieldsArray(fa, fields);
                }
                else if (obj.TryGetPropertyValue("keys", out var keysNode) && keysNode is JsonArray ka)
                {
                    ReadFieldsArray(ka, fields);
                }
                else if (obj.TryGetPropertyValue("columns", out var colsNode) && colsNode is JsonArray ca)
                {
                    ReadFieldsArray(ca, fields);
                }
            }

            if (obj.TryGetPropertyValue("ignoreNulls", out var inNode) && inNode is JsonValue inv && inv.TryGetValue<bool>(out var inBool))
            {
                ignoreNulls = inBool;
            }

            if (obj.TryGetPropertyValue("ignoreAllNull", out var ianNode) && ianNode is JsonValue ianv && ianv.TryGetValue<bool>(out var ianBool))
            {
                ignoreAllNull = ianBool;
            }

            if (obj.TryGetPropertyValue("caseInsensitive", out var ciNode) && ciNode is JsonValue civ && civ.TryGetValue<bool>(out var ciBool))
            {
                caseInsensitive = ciBool;
            }
        }

        return new UniqueConfig(fields, ignoreNulls, ignoreAllNull, caseInsensitive);
    }

    private static void ReadFieldsArray(JsonArray arr, List<string> target)
    {
        foreach (var item in arr)
        {
            if (item is JsonValue val && val.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            {
                target.Add(s.Trim());
            }
        }
    }

    private sealed record UniqueConfig(
        IReadOnlyList<string> Fields,
        bool IgnoreNulls,
        bool IgnoreAllNull,
        bool CaseInsensitive);
}
