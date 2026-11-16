namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Represents a candidate contract bundle read from the staging/draft schema blob container
/// (supporting both paired .schema.json + .rules.json and direct .odcs.yaml contracts).
/// </summary>
public sealed record DraftSchemaArtifact(
    string TriggerBlobPath,
    string? SchemaName,
    int? Version,
    string? RawSchemaJson,
    string? RawRulesJson,
    string? RawOdcsYaml,
    IReadOnlyList<string> SampleValidFixtures,
    IReadOnlyList<string> SampleInvalidFixtures,
    IReadOnlyList<string> ConsumedDraftBlobPaths,
    bool IsIgnoredCompanionBlob = false);
