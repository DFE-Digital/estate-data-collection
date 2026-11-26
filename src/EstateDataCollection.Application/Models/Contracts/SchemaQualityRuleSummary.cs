namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Summary of a quality or business rule attached to a schema version.
/// </summary>
/// <param name="Id">Unique rule identifier within the schema contract.</param>
/// <param name="Dimension">Data quality dimension (e.g. `completeness`, `validity`, `consistency`).</param>
/// <param name="Severity">Rule severity (`error` or `warning`).</param>
/// <param name="TargetPath">Primary JSON Pointer or property path evaluated by the rule.</param>
/// <param name="Engine">Validation engine identifier responsible for executing this rule.</param>
/// <param name="ErrorMessage">Message returned to callers when the rule is violated.</param>
/// <param name="Description">Optional detailed explanation of what the rule validates and why.</param>
public sealed record SchemaQualityRuleSummary(
    string Id,
    string? Dimension,
    string? Severity,
    string? TargetPath,
    string? Engine,
    string ErrorMessage,
    string? Description = null
);
