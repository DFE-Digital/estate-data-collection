using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Abstraction;

public interface ISchemaRegistry : IInfrastructure
{
    /// <summary>
    /// Resolves the version active at the specified timestamp (for non-explicit submissions).
    /// </summary>
    Task<Result<ResolvedSchema>> ResolveActiveSchemaAsync(string schemaName, DateTimeOffset timestamp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a specific version explicitly requested by a client or downstream worker.
    /// </summary>
    Task<Result<ResolvedSchema>> GetExplicitSchemaAsync(string schemaName, int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all schema metadata entries in the registry (optionally filtered by schema name).
    /// </summary>
    Task<Result<IReadOnlyList<SchemaMetadata>>> ListSchemasAsync(
        string? schemaNameFilter = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Result<IReadOnlyList<SchemaMetadata>>>(Array.Empty<SchemaMetadata>());
    }

    /// <summary>
    /// Updates the lifecycle schedule / state metadata (.meta.json) for an existing schema version.
    /// </summary>
    Task<Result> UpdateSchemaMetadataAsync(
        SchemaMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        return SaveSchemaAsync(metadata, cancellationToken);
    }

    /// <summary>
    /// Updates the stored valid/invalid golden test fixtures (.valid.json / .invalid.json) for an existing schema version.
    /// </summary>
    Task<Result> UpdateSchemaFixturesAsync(
        string schemaName,
        int version,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Validates a candidate schema and/or companion quality rules (JsonLogic, CEL) and executes dry-run golden test fixtures.
    /// </summary>
    Task<Result<SchemaVerificationResult>> VerifyCanditateSchemaAsync(
        string? rawSchemaJson,
        string? rawRulesJson,
        IReadOnlyList<string> sampleValidPayloads,
        IReadOnlyList<string> sampleInvalidPayloads,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a candidate schema, quality rules, identifier pointer alignment against the schema,
    /// and executes dry-run golden test fixtures.
    /// </summary>
    Task<Result<SchemaVerificationResult>> VerifyCanditateSchemaAsync(
        string? rawSchemaJson,
        string? rawRulesJson,
        IReadOnlyList<string> sampleValidPayloads,
        IReadOnlyList<string> sampleInvalidPayloads,
        SchemaIdentifierConfig? identifierConfig,
        CancellationToken cancellationToken = default)
    {
        return VerifyCanditateSchemaAsync(
            rawSchemaJson,
            rawRulesJson,
            sampleValidPayloads,
            sampleInvalidPayloads,
            cancellationToken);
    }

    /// <summary>
    /// Checks whether a schema version already exists in storage.
    /// </summary>
    Task<Result<bool>> ExistsAsync(string schemaName, int version, CancellationToken cancellationToken = default);

    Task<Result> SaveSchemaAsync(SchemaMetadata metadata, CancellationToken cancellationToken = default);

    Task<Result> SaveSchemaAsync(
        SchemaMetadata metadata,
        string? rawRulesJson,
        string? rawOdcsYaml = null,
        CancellationToken cancellationToken = default);

    Task<Result> SaveSchemaAsync(
        SchemaMetadata metadata,
        string? rawRulesJson,
        string? rawOdcsYaml,
        IReadOnlyList<string>? sampleValidFixtures,
        IReadOnlyList<string>? sampleInvalidFixtures,
        CancellationToken cancellationToken = default)
    {
        return SaveSchemaAsync(metadata, rawRulesJson, rawOdcsYaml, cancellationToken);
    }

    /// <summary>
    /// Reads a newly uploaded contract (ODCS YAML, .schema.json, or .rules.json) and any companion files
    /// (.meta.json, .valid.json, .invalid.json) from the draft/staging schema container.
    /// </summary>
    Task<Result<DraftSchemaArtifact>> GetDraftArtifactAsync(
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Result<DraftSchemaArtifact>>(
            Error.NotFound(ErrorCodes.SchemaNotFound, new { blobPath }));
    }

    /// <summary>
    /// Deletes the processed draft contract files (and any prior .error.json diagnostic blob) from the draft container
    /// after successful promotion to the active schemas container.
    /// </summary>
    Task<Result> CompleteDraftIngestionAsync(
        DraftSchemaArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Writes a .error.json diagnostic file alongside the failed draft blob in the draft container.
    /// </summary>
    Task<Result> RecordDraftIngestionFailureAsync(
        string blobPath,
        string? schemaName,
        int? version,
        IReadOnlyList<string> errors,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success());
    }
}
