using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;

public sealed record GetSubmissionDetailResult(
    SubmissionSession Session,
    string RawSessionJson,
    IReadOnlyList<SubmissionDraftItem> DraftItems,
    string? MergedDraftPayloadJson,
    SubmissionProcessedArtifact? ProcessedArtifact,
    bool IsReadyToFinalise,
    IReadOnlyList<ValidationError> PendingErrors,
    IReadOnlyList<string> NextSteps,
    string? NextStepsSummary,
    string? ResolvedIdentifierType,
    string? ResolvedIdentifierValue,
    int? ResolvedUrn,
    IReadOnlyList<SubmissionAuditEntry> AuditEntries);
