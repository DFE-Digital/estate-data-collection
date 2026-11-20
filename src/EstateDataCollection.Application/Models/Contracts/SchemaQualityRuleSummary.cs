namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Summary of a quality / business rule attached to a schema version.
/// </summary>
public sealed record SchemaQualityRuleSummary(
    string Id,
    string? Dimension,
    string? Severity,
    string? TargetPath,
    string? Engine,
    string ErrorMessage,
    string? Description = null
);