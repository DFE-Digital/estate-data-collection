using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EstateDataCollection.Application.Validators.Logic;

/// <summary>
/// Unified quality / business validation rule supporting both ODCS v3.2.0 quality checks
/// (id, type, engine, description, implementation) and standalone rules.json files
/// (id, description, severity, path, message, engine, implementation/assert).
/// </summary>
public sealed class BusinessRule
{
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// ODCS rule type (e.g. "custom", "library", "sql", "text"). Defaults to "custom".
    /// </summary>
    public string Type { get; init; } = "custom";

    /// <summary>
    /// Target validation engine (e.g. "jsonlogic", "cel").
    /// When omitted in standalone rules, falls back to ValidationOptions.DefaultEngine ("jsonlogic").
    /// </summary>
    public string? Engine { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Severity { get; init; } = "ERROR";

    /// <summary>
    /// Dot-separated target path within the payload (e.g. "survey", "elements[].occurrences[]").
    /// Empty or "/" targets the root object.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Optional explicit failure message. Falls back to <see cref="Description"/> when omitted (as in ODCS v3.2.0).
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// ODCS v3.2.0 "implementation" rule definition (JsonObject for JsonLogic, string for CEL).
    /// </summary>
    [JsonPropertyName("implementation")]
    public JsonNode? Implementation { get; init; }

    /// <summary>
    /// Standalone rules alias for <see cref="Implementation"/>.
    /// </summary>
    [JsonPropertyName("assert")]
    public JsonNode? Assert { get; init; }

    [JsonIgnore]
    public JsonNode? EffectiveAssertion
    {
        get { return Implementation ?? Assert; }
    }

    [JsonIgnore]
    public string EffectiveMessage
    {
        get { return !string.IsNullOrWhiteSpace(Message) ? Message : Description; }
    }

    public string ResolveEngine(string defaultEngine = "jsonlogic")
    {
        return !string.IsNullOrWhiteSpace(Engine) ? Engine.Trim() : defaultEngine;
    }
}
