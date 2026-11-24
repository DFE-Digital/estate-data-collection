using System.Text.Json.Nodes;
using EstateDataCollection.Common.Markers;

namespace EstateDataCollection.Application.Abstraction;

/// <summary>
/// Provides domain-specific ambient lookup data and per-target derived helper properties
/// for rule evaluation, keeping domain assumptions decoupled from rule engine orchestration.
/// </summary>
public interface IRuleContextEnricher : IInfrastructure
{
    /// <summary>
    /// Extracts payload-wide lookup data once per payload validation pass.
    /// </summary>
    void PopulateAmbientContext(JsonNode rootNode, IDictionary<string, JsonNode?> ambientContext) { }

    /// <summary>
    /// Enriches a resolved evaluation target node before individual rules evaluate against it.
    /// </summary>
    void EnrichTargetContext(JsonObject enrichedTarget, JsonNode? childFocusNode, JsonNode rootNode) { }
}
