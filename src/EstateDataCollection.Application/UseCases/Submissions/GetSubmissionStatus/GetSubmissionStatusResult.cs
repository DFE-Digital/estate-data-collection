using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;

public sealed record GetSubmissionStatusResult(
    Guid SubmissionId,
    string Status,
    string SchemaName,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinalisedAtUtc,
    bool HasDraftPayload,
    bool IsReadyToFinalise,
    IReadOnlyList<ValidationError> PendingErrors,
    int? Urn = null,
    string? Identifier = null,
    string? IdentifierType = null);
