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
        JsonNode? ChildFocusNode);

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
            results.Add(new ResolvedRuleTarget("/", root, root));
            return results;
        }

        var segments = pathPattern.Split('.', StringSplitOptions.RemoveEmptyEntries);
        TraverseSegments(root, segments, 0, "", results);
        return results;
    }

    public static JsonObject CreateEnrichedContextNode(
        JsonNode contextNode,
        JsonNode? childFocusNode,
        JsonNode rootNode,
        IReadOnlyDictionary<string, JsonNode?> ambientContext,
        IReadOnlyList<IRuleContextEnricher> enrichers)
    {
        var enriched = contextNode.DeepClone() as JsonObject ?? new JsonObject();

        // If the rule targets a nested object property (e.g. "survey" or "replacement"),
        // merge that child object's properties into the context so both relative ("updated_at")
        // and parent-qualified ("survey.updated_at") references resolve cleanly.
        if (childFocusNode is JsonObject childObj && !ReferenceEquals(contextNode, childFocusNode))
        {
            foreach (var (key, value) in childObj)
            {
                if (!enriched.ContainsKey(key))
                {
                    enriched[key] = value?.DeepClone();
                }
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

    private static void TraverseSegments(
        JsonNode currentNode,
        string[] segments,
        int segmentIndex,
        string currentJsonPointer,
        List<ResolvedRuleTarget> accumulator)
    {
        if (segmentIndex >= segments.Length)
        {
            accumulator.Add(new ResolvedRuleTarget(currentJsonPointer, currentNode, currentNode));
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
            accumulator.Add(new ResolvedRuleTarget(targetPath, currentNode, childNode));
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
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is { } item)
                    {
                        TraverseSegments(item, segments, segmentIndex + 1, $"{nextPointer}/{i}", accumulator);
                    }
                }
            }
        }
        else
        {
            TraverseSegments(nextNode, segments, segmentIndex + 1, nextPointer, accumulator);
        }
    }
}
