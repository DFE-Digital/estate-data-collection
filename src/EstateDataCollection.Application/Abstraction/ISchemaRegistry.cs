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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the lifecycle schedule / state metadata (.meta.json) for an existing schema version.
    /// </summary>
    Task<Result> UpdateSchemaMetadataAsync(
        SchemaMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the stored valid/invalid golden test fixtures (.valid.json / .invalid.json) for an existing schema version.
    /// </summary>
    Task<Result> UpdateSchemaFixturesAsync(
        string schemaName,
        int version,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a schema version key has already been consumed in storage (including Deleted tombstones).
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a <c>{schemaName}/v{version}.error.json</c> diagnostic report in the schemas container.
    /// </summary>
    Task<Result> SaveErrorReportAsync(
        SchemaErrorReport report,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a <c>{schemaName}/v{version}.error.json</c> diagnostic report if present.
    /// </summary>
    Task<Result<SchemaErrorReport?>> GetErrorReportAsync(
        string schemaName,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a <c>{schemaName}/v{version}.error.json</c> diagnostic report if present.
    /// </summary>
    Task<Result> DeleteErrorReportAsync(
        string schemaName,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes contract/rule/fixture blobs for a schema version while marking <c>.meta.json</c> as
    /// <see cref="SchemaLifecycleState.Deleted"/> and preserving <c>.meta.json</c> + <c>.error.json</c>.
    /// </summary>
    Task<Result<SchemaMetadata>> DeleteSchemaVersionAsync(
        string schemaName,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a newly uploaded contract (ODCS YAML, .schema.json, or .rules.json) and any companion files
    /// (.meta.json, .valid.json, .invalid.json) from the draft/staging schema container.
    /// </summary>
    Task<Result<DraftSchemaArtifact>> GetDraftArtefactAsync(
        string blobPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the processed draft contract files (and any prior .error.json diagnostic blob) from the draft container
    /// after successful promotion to the active schemas container.
    /// </summary>
    Task<Result> CompleteDraftIngestionAsync(
        DraftSchemaArtifact artifact,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a .error.json diagnostic file alongside the failed draft blob in the draft container.
    /// </summary>
    Task<Result> RecordDraftIngestionFailureAsync(
        string blobPath,
        string? schemaName,
        int? version,
        IReadOnlyList<string> errors,
        CancellationToken cancellationToken = default);
}
