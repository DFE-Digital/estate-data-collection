using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;

namespace EstateDataCollection.Infrastructure.Validation;

/// <summary>
/// Resolves dot/array path patterns (e.g. <c>elements[].occurrences[]</c>) within a JSON payload
/// and builds isolated, enriched evaluation contexts via registered <see cref="IRuleContextEnricher"/> instances.
/// </summary>
public static class RuleTargetResolver
{
    public readonly record struct ResolvedRuleTarget(
        string InstancePath,
        JsonNode ContextNode,
        JsonNode? ChildFocusNode,
        IReadOnlyList<JsonNode>? Ancestors = null,
        int? Index = null);

    public static IReadOnlyDictionary<string, JsonNode?> BuildAmbientContext(
        JsonNode rootNode,
        IReadOnlyList<IRuleContextEnricher> enrichers)
    {
        var ambientContext = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var enricher in enrichers)
        {
            enricher.PopulateAmbientContext(rootNode, ambientContext);
        }

        return ambientContext;
    }

    public static List<ResolvedRuleTarget> ResolvePathTargets(JsonNode root, string pathPattern)
    {
        var results = new List<ResolvedRuleTarget>();
        if (string.IsNullOrWhiteSpace(pathPattern) || pathPattern == "/")
        {
            results.Add(new ResolvedRuleTarget("/", root, root, [], Index: null));
            return results;
        }

        var segments = pathPattern.Split('.', StringSplitOptions.RemoveEmptyEntries);
        TraverseSegments(root, segments, 0, "", [], null, results);
        return results;
    }

    public static JsonObject CreateEnrichedContextNode(
        JsonNode contextNode,
        JsonNode? childFocusNode,
        JsonNode rootNode,
        IReadOnlyDictionary<string, JsonNode?> ambientContext,
        IReadOnlyList<IRuleContextEnricher> enrichers)
    {
        return CreateEnrichedContextNode(
            contextNode,
            childFocusNode,
            rootNode,
            ambientContext,
            enrichers,
            ancestors: null,
            index: null);
    }

    public static JsonObject CreateEnrichedContextNode(
        JsonNode contextNode,
        JsonNode? childFocusNode,
        JsonNode rootNode,
        IReadOnlyDictionary<string, JsonNode?> ambientContext,
        IReadOnlyList<IRuleContextEnricher> enrichers,
        IReadOnlyList<JsonNode>? ancestors,
        int? index)
    {
        var enriched = contextNode.DeepClone() as JsonObject ?? new JsonObject();

        // If the rule targets a nested object property (e.g. "survey" or "replacement"),
        // merge that child object's properties into the context so both relative ("updated_at")
        // and parent-qualified ("survey.updated_at") references resolve cleanly.
        if (childFocusNode is JsonObject childObj && !ReferenceEquals(contextNode, childFocusNode))
        {
            foreach (var (key, value) in childObj)
            {
                enriched[key] = value?.DeepClone();
            }
        }

        // Attach root top-level properties (survey, school, sites, elements, answers, evidence)
        // when not already present on the local target node.
        if (rootNode is JsonObject rootObj && !ReferenceEquals(contextNode, rootNode))
        {
            foreach (var (key, value) in rootObj)
            {
                if (!enriched.ContainsKey(key) && value is not null)
                {
                    enriched[key] = value.DeepClone();
                }
            }
        }

        // Inject hierarchical ancestor & positional context ($parent, $ancestors, $root, $index)
        // without clobbering real domain payload properties.
        enriched["$parent"] = BuildParentChain(ancestors);
        enriched["$ancestors"] = BuildAncestorsArray(ancestors, rootNode);
        enriched["$root"] = rootNode.DeepClone();
        if (index.HasValue)
        {
            enriched["$index"] = JsonValue.Create(index.Value);
        }

        // Server-derived ambient context (e.g. valid_site_ids, valid_block_ids, valid_evidence_ids)
        // must overwrite any identically named keys in the submitter payload so clients cannot spoof them.
        foreach (var (key, value) in ambientContext)
        {
            if (value is not null)
            {
                enriched[key] = value.DeepClone();
            }
        }

        foreach (var enricher in enrichers)
        {
            enricher.EnrichTargetContext(enriched, childFocusNode, rootNode);
        }

        return enriched;
    }

    private static JsonNode? BuildParentChain(IReadOnlyList<JsonNode>? ancestors)
    {
        if (ancestors is not { Count: > 0 })
        {
            return null;
        }

        // Build singly-linked $parent chain from outermost (index 0) to immediate parent (index Count - 1)
        // so both $parent.field and $parent.$parent.field resolve cleanly.
        JsonNode? currentParent = null;
        for (var i = 0; i < ancestors.Count; i++)
        {
            var cloned = ancestors[i].DeepClone();
            if (cloned is JsonObject obj && currentParent is not null)
            {
                obj["$parent"] = currentParent;
            }
            currentParent = cloned;
        }

        return currentParent;
    }

    private static JsonArray BuildAncestorsArray(IReadOnlyList<JsonNode>? ancestors, JsonNode rootNode)
    {
        var arr = new JsonArray();
        if (ancestors is not { Count: > 0 })
        {
            return arr;
        }

        foreach (var ancestor in ancestors)
        {
            if (!ReferenceEquals(ancestor, rootNode))
            {
                arr.Add(ancestor.DeepClone());
            }
        }

        return arr;
    }

    private static void TraverseSegments(
        JsonNode currentNode,
        string[] segments,
        int segmentIndex,
        string currentJsonPointer,
        List<JsonNode> ancestors,
        int? currentIndex,
        List<ResolvedRuleTarget> accumulator)
    {
        if (segmentIndex >= segments.Length)
        {
            accumulator.Add(new ResolvedRuleTarget(
                currentJsonPointer,
                currentNode,
                currentNode,
                ancestors.ToArray(),
                currentIndex));
            return;
        }

        var segment = segments[segmentIndex];
        var isArrayTarget = segment.EndsWith("[]", StringComparison.Ordinal);
        var propertyName = isArrayTarget ? segment[..^2] : segment;

        // If this is the last segment and it is a property on the current context object (not an array []),
        // the context node for rule evaluation includes both the parent object and (if an object) the child property object,
        // while the reported failure path points to the target property.
        if (!isArrayTarget && segmentIndex == segments.Length - 1 && currentNode is JsonObject currentObj)
        {
            var targetPath = $"{currentJsonPointer}/{propertyName}";
            currentObj.TryGetPropertyValue(propertyName, out var childNode);
            JsonNode[] targetAncestors = [.. ancestors, currentNode];
            accumulator.Add(new ResolvedRuleTarget(
                targetPath,
                currentNode,
                childNode,
                targetAncestors,
                currentIndex));
            return;
        }

        if (currentNode is not JsonObject obj ||
            !obj.TryGetPropertyValue(propertyName, out var nextNode) ||
            nextNode is null)
        {
            return;
        }

        var nextPointer = $"{currentJsonPointer}/{propertyName}";

        if (isArrayTarget)
        {
            if (nextNode is JsonArray arr)
            {
                ancestors.Add(currentNode);
                try
                {
                    for (var i = 0; i < arr.Count; i++)
                    {
                        if (arr[i] is { } item)
                        {
                            TraverseSegments(item, segments, segmentIndex + 1, $"{nextPointer}/{i}", ancestors, i, accumulator);
                        }
                    }
                }
                finally
                {
                    ancestors.RemoveAt(ancestors.Count - 1);
                }
            }
        }
        else
        {
            ancestors.Add(currentNode);
            try
            {
                TraverseSegments(nextNode, segments, segmentIndex + 1, nextPointer, ancestors, currentIndex, accumulator);
            }
            finally
            {
                ancestors.RemoveAt(ancestors.Count - 1);
            }
        }
    }
}
