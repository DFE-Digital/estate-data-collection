namespace EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;

/// <summary>
/// Request to ingest a contract blob dropped into the draft-schemas container.
/// Effective dates are never on the schema packs; they are assigned later via the Internal Admin API.
/// </summary>
public sealed record IngestDraftSchemaRequest(string BlobPath);
