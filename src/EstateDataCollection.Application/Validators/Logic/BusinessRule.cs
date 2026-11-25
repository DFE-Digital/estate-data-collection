using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Logic;

/// <summary>
/// Unified quality / business validation rule supporting both ODCS v3.2.0 quality checks
/// (id, type, engine, description, implementation) and standalone rules.json files
/// (id, description, severity, path, message, engine, implementation/assert, stage, order, dependsOn, stopOnFailure, action).
/// </summary>
public sealed class BusinessRule
{
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// ODCS rule type (e.g. "custom", "library", "unique", "sql", "text"). Defaults to "custom".
    /// </summary>
    public string Type { get; init; } = "custom";

    /// <summary>
    /// Target validation engine (e.g. "jsonlogic", "cel", "unique").
    /// When omitted in standalone rules, falls back to ValidationOptions.DefaultEngine ("jsonlogic").
    /// </summary>
    public string? Engine { get; init; }

    public string Description { get; init; } = string.Empty;

    public RuleSeverity Severity { get; init; } = RuleSeverity.Error;

    /// <summary>
    /// Runbook execution stage (lower executes earlier). Defaults to 100.
    /// </summary>
    public int Stage { get; init; } = 100;

    /// <summary>
    /// Ordering hint within a stage (lower executes earlier). Defaults to 0.
    /// </summary>
    public int Order { get; init; } = 0;

    /// <summary>
    /// Optional upstream rule IDs that must succeed for this rule to run.
    /// If any dependency failed, this rule is skipped to suppress cascading noise.
    /// </summary>
    public IReadOnlyList<string>? DependsOn { get; init; }

    /// <summary>
    /// Whether failure halts subsequent stages. Defaults to true when Severity == RuleSeverity.Critical.
    /// </summary>
    public bool? StopOnFailure { get; init; }

    [JsonIgnore]
    public bool EffectiveStopOnFailure
    {
        get { return StopOnFailure ?? (Severity == RuleSeverity.Critical); }
    }

    /// <summary>
    /// Optional explicit action metadata (e.g. "reject_submission", "highlight_issue", "allow_submission").
    /// </summary>
    public string? Action { get; init; }

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

    [JsonIgnore]
    public string EffectiveAction
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Action))
            {
                return Action.Trim();
            }

            return Severity switch
            {
                RuleSeverity.Critical => "reject_submission",
                RuleSeverity.Error => "reject_submission",
                RuleSeverity.Warning => "highlight_issue",
                RuleSeverity.Info => "allow_submission",
                _ => "reject_submission"
            };
        }
    }

    public string ResolveEngine(string defaultEngine = "jsonlogic")
    {
        return !string.IsNullOrWhiteSpace(Engine) ? Engine.Trim() : defaultEngine;
    }
}
