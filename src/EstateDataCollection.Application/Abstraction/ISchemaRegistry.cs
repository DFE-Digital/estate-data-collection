using EstateDataCollection.Application.Contracts;
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
    /// Validates a candidate schema against Draft 2020-12 and executes dry-run golden test fixtures.
    /// </summary>
    Task<Result<SchemaVerificationResult>> VerifyCanditateSchemaAsync(string rawSchemaJson,
        IReadOnlyList<string> sampleValidPayloads, IReadOnlyList<string> sampleInvalidPayloads,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a schema version already exists in storage.
    /// </summary>
    Task<Result<bool>> ExistsAsync(string schemaName, int version, CancellationToken cancellationToken = default);

    Task<Result> SaveSchemaAsync(SchemaMetadata metadata, CancellationToken cancellationToken = default);
}