namespace EstateDataCollection.Domain.Schema;

/// <summary>
/// Per-schema-version metadata configuration for extracting an establishment identifier (e.g. URN)
/// from a submission payload. Stored in {schemaName}/v{version}.meta.json so that ODCS data contracts
/// remain agnostic of EDC storage partitioning and identifier extraction mechanics.
/// </summary>
public sealed record SchemaIdentifierConfig
{
    /// <summary>
    /// Identifier type (e.g. "URN", "UKPRN", "LAEstab", "None"). Defaults to "URN".
    /// </summary>
    public string Type { get; init; } = "URN";

    /// <summary>
    /// JSON Pointer or dot-notation path to the identifier within the submission payload
    /// (e.g. "/school/urn", "/urn", "school.urn"). When null or empty, falls back to platform defaults.
    /// </summary>
    public string? JsonPointer { get; init; }

    /// <summary>
    /// Optional per-schema-version flag indicating whether a valid identifier is required on finalisation.
    /// When null, falls back to ValidationOptions.RequireUrnOnFinalise.
    /// </summary>
    public bool? Required { get; init; }
}
