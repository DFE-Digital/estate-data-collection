using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Domain.Submission;

/// <summary>
/// Merges chronologically ordered draft JSON payloads using RFC 7396 recursive JSON Merge Patch semantics:
/// - JSON objects deep-merge recursively by property name;
/// - JSON arrays and primitive values from newer drafts replace older values at the same path;
/// - Explicit JSON null values in subsequent patches remove the target property.
/// </summary>
public static class DraftPayloadMerger
{
    public static Result<string> Merge(IReadOnlyList<SubmissionDraftItem> items)
    {
        if (items is null || items.Count == 0)
        {
            return Error.Failure(ErrorCodes.PayloadEmpty);
        }

        var orderedPayloads = items
            .OrderBy(i => i.CreatedAtUtc)
            .ThenBy(i => i.BlobName, StringComparer.Ordinal)
            .Select(i => i.PayloadJson)
            .ToList();

        return MergeJsonPayloads(orderedPayloads);
    }

    public static Result<string> MergeJsonPayloads(IReadOnlyList<string> payloads)
    {
        if (payloads is null || payloads.Count == 0)
        {
            return Error.Failure(ErrorCodes.PayloadEmpty);
        }

        if (payloads.Count == 1)
        {
            var single = payloads[0];
            if (string.IsNullOrWhiteSpace(single))
            {
                return Error.Failure(ErrorCodes.PayloadEmpty);
            }

            try
            {
                _ = JsonNode.Parse(single);
                return single;
            }
            catch (JsonException ex)
            {
                return Error.Validation(
                    ErrorCodes.JsonMalformed,
                    new { Message = $"Draft payload is not valid JSON: {ex.Message}" });
            }
        }

        JsonNode? merged = null;

        for (var i = 0; i < payloads.Count; i++)
        {
            var raw = payloads[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            JsonNode? next;
            try
            {
                next = JsonNode.Parse(raw);
            }
            catch (JsonException ex)
            {
                return Error.Validation(
                    ErrorCodes.JsonMalformed,
                    new { Message = $"Draft payload #{i + 1} is not valid JSON: {ex.Message}" });
            }

            merged = merged is null ? next : ApplyMergePatch(merged, next);
        }

        if (merged is null)
        {
            return Error.Failure(ErrorCodes.PayloadEmpty);
        }

        return merged.ToJsonString();
    }

    private static JsonNode? ApplyMergePatch(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObj)
        {
            return patch?.DeepClone();
        }

        var targetObj = target as JsonObject ?? new JsonObject();

        foreach (var kvp in patchObj)
        {
            if (kvp.Value is null)
            {
                targetObj.Remove(kvp.Key);
                continue;
            }

            if (targetObj.TryGetPropertyValue(kvp.Key, out var existingValue) &&
                existingValue is JsonObject &&
                kvp.Value is JsonObject)
            {
                targetObj[kvp.Key] = ApplyMergePatch(existingValue, kvp.Value);
            }
            else
            {
                targetObj[kvp.Key] = kvp.Value.DeepClone();
            }
        }

        return targetObj;
    }
}
