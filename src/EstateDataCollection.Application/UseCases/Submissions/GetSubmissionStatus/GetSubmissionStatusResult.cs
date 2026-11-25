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
    string? Identifier = null,
    string? IdentifierType = null,
    int DraftCount = 0,
    DateTimeOffset? LastDraftAtUtc = null,
    IReadOnlyList<string>? NextSteps = null,
    string? Summary = null,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null);
