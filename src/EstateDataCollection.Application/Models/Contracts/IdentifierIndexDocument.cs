namespace EstateDataCollection.Application.Models.Contracts;

public sealed record IdentifierIndexDocument(
    string IdentifierType,
    string IdentifierValue,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<IdentifierIndexEntry> Submissions);