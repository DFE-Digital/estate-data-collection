using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;

/// <summary>
/// Result of ingesting a draft schema/rules/ODCS contract from the draft-schemas container.
/// </summary>
public sealed record IngestDraftSchemaResult(
    string BlobPath,
    string SchemaName,
    int Version,
    bool Promoted,
    string Status,
    Node<int>? State = null,
    string? Sha256Hash = null,
    bool HasErrorReport = false,
    string? ErrorSummary = null,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null);
