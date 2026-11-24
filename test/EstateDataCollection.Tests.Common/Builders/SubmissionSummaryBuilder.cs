using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SubmissionSummaryBuilder
{
    private Guid _submissionId = Guid.NewGuid();
    private string _schemaName = "edc_payload";
    private int _schemaVersion = 1;
    private DateTimeOffset _createdAtUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset? _finalisedAtUtc;
    private DateTimeOffset _lastActivityAtUtc = DateTimeOffset.UtcNow;
    private bool _isFinalised;
    private string _lifecycleState = "Draft";
    private int? _urn;
    private string? _identifierType;
    private string? _identifierValue;
    private bool _hasDraftPayload;
    private int _draftCount;
    private DateTimeOffset? _lastDraftAtUtc;
    private int _auditEventCount = 1;
    private string? _lastAuditAction = "CreateSubmission";
    private string? _lastAuditOutcome = "Succeeded";
    private int? _lastAuditHttpStatus = 201;
    private DateTimeOffset? _lastAuditAtUtc = DateTimeOffset.UtcNow;

    public SubmissionSummaryBuilder WithId(Guid submissionId)
    {
        _submissionId = submissionId;
        return this;
    }

    public SubmissionSummaryBuilder WithSchema(string schemaName, int version = 1)
    {
        _schemaName = schemaName;
        _schemaVersion = version;
        return this;
    }

    public SubmissionSummaryBuilder CreatedAt(DateTimeOffset createdAtUtc)
    {
        _createdAtUtc = createdAtUtc;
        return this;
    }

    public SubmissionSummaryBuilder LastActivityAt(DateTimeOffset lastActivityAtUtc)
    {
        _lastActivityAtUtc = lastActivityAtUtc;
        return this;
    }

    public SubmissionSummaryBuilder Finalised(DateTimeOffset? finalisedAtUtc = null)
    {
        _isFinalised = true;
        _lifecycleState = "Finalised";
        _finalisedAtUtc = finalisedAtUtc ?? DateTimeOffset.UtcNow;
        return this;
    }

    public SubmissionSummaryBuilder WithLifecycleState(string lifecycleState)
    {
        _lifecycleState = lifecycleState;
        return this;
    }

    public SubmissionSummaryBuilder WithUrn(int urn, string? identifierType = "URN", string? identifierValue = null)
    {
        _urn = urn;
        _identifierType = identifierType;
        _identifierValue = identifierValue ?? urn.ToString();
        return this;
    }

    public SubmissionSummaryBuilder WithIdentifier(string? type, string? value, int? urn = null)
    {
        _identifierType = type;
        _identifierValue = value;
        _urn = urn;
        return this;
    }

    public SubmissionSummaryBuilder WithDrafts(int count, DateTimeOffset? lastDraftAtUtc = null, bool hasDraftPayload = true)
    {
        _draftCount = count;
        _lastDraftAtUtc = lastDraftAtUtc;
        _hasDraftPayload = hasDraftPayload;
        return this;
    }

    public SubmissionSummaryBuilder WithLastAudit(
        string action,
        string outcome,
        int httpStatus,
        DateTimeOffset? atUtc = null,
        int totalEvents = 1)
    {
        _lastAuditAction = action;
        _lastAuditOutcome = outcome;
        _lastAuditHttpStatus = httpStatus;
        _lastAuditAtUtc = atUtc;
        _auditEventCount = totalEvents;
        return this;
    }

    public SubmissionSummary Build()
    {
        return new SubmissionSummary(
            SubmissionId: _submissionId,
            SchemaName: _schemaName,
            SchemaVersion: _schemaVersion,
            CreatedAtUtc: _createdAtUtc,
            FinalisedAtUtc: _finalisedAtUtc,
            LastActivityAtUtc: _lastActivityAtUtc,
            IsFinalised: _isFinalised,
            LifecycleState: _lifecycleState,
            Urn: _urn,
            IdentifierType: _identifierType,
            IdentifierValue: _identifierValue,
            HasDraftPayload: _hasDraftPayload,
            DraftCount: _draftCount,
            LastDraftAtUtc: _lastDraftAtUtc,
            AuditEventCount: _auditEventCount,
            LastAuditAction: _lastAuditAction,
            LastAuditOutcome: _lastAuditOutcome,
            LastAuditHttpStatus: _lastAuditHttpStatus,
            LastAuditAtUtc: _lastAuditAtUtc);
    }
}
