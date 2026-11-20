namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionSummary(
    Guid SubmissionId,
    string SchemaName,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinalisedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    bool IsFinalised,
    string LifecycleState,
    int? Urn,
    string? IdentifierType,
    string? IdentifierValue,
    bool HasDraftPayload,
    int DraftCount,
    DateTimeOffset? LastDraftAtUtc,
    int AuditEventCount,
    string? LastAuditAction,
    string? LastAuditOutcome,
    int? LastAuditHttpStatus,
    DateTimeOffset? LastAuditAtUtc);
