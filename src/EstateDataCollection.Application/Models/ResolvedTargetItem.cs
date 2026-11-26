using System.Text.Json.Nodes;

namespace EstateDataCollection.Application.Models;

public sealed record ResolvedTargetItem(
    string TargetPath,
    JsonNode EnrichedContextNode,
    JsonNode? ChildFocusNode = null,
    IReadOnlyList<JsonNode>? Ancestors = null,
    int? Index = null,
    IReadOnlyDictionary<string, JsonNode?>? AmbientContext = null);