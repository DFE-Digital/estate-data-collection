namespace EstateDataCollection.Application.Models.Contracts;

public sealed record IdentifierIndexEntry(
    Guid SubmissionId,
    string SchemaName,
    int SchemaVersion,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    string? PayloadPath = null,
    string? ProcessedBundlePrefix = null);

public sealed record IdentifierIndexDocument(
    string IdentifierType,
    string IdentifierValue,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<IdentifierIndexEntry> Submissions);
