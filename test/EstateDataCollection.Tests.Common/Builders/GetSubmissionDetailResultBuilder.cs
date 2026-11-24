using System.Text;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class GetSubmissionDetailResultBuilder
{
    private SubmissionSession _session = SubmissionSession.Create("edc_payload", 1);
    private string _rawSessionJson = """{"submissionId":"1"}""";
    private readonly List<SubmissionDraftItem> _draftItems = [];
    private string? _mergedDraftPayloadJson;
    private SubmissionProcessedArtifact? _processedArtifact;
    private bool _isReadyToFinalise;
    private readonly List<ValidationError> _pendingErrors = [];
    private readonly List<string> _nextSteps = [];
    private string? _nextStepsSummary;
    private string? _resolvedIdentifierType;
    private string? _resolvedIdentifierValue;
    private readonly List<SubmissionAuditEntry> _auditEntries = [];

    public GetSubmissionDetailResultBuilder WithSession(SubmissionSession session)
    {
        _session = session;
        return this;
    }

    public GetSubmissionDetailResultBuilder WithRawSessionJson(string rawSessionJson)
    {
        _rawSessionJson = rawSessionJson;
        return this;
    }

    public GetSubmissionDetailResultBuilder WithDraftItem(
        string blobName,
        string payloadJson,
        DateTimeOffset? createdAtUtc = null,
        long? sizeBytes = null)
    {
        _draftItems.Add(new SubmissionDraftItem(
            blobName,
            createdAtUtc ?? DateTimeOffset.UtcNow,
            payloadJson,
            sizeBytes ?? Encoding.UTF8.GetByteCount(payloadJson)));
        return this;
    }

    public GetSubmissionDetailResultBuilder WithMergedDraftPayload(string? mergedDraftPayloadJson)
    {
        _mergedDraftPayloadJson = mergedDraftPayloadJson;
        return this;
    }

    public GetSubmissionDetailResultBuilder WithProcessedArtefact(SubmissionProcessedArtifact? processedArtifact)
    {
        _processedArtifact = processedArtifact;
        return this;
    }

    public GetSubmissionDetailResultBuilder ReadyToFinalise(bool ready = true)
    {
        _isReadyToFinalise = ready;
        return this;
    }

    public GetSubmissionDetailResultBuilder WithPendingError(string propertyPath, string message, string? ruleId = null)
    {
        _pendingErrors.Add(new ValidationError(propertyPath, message, ruleId));
        return this;
    }

    public GetSubmissionDetailResultBuilder WithNextSteps(string? summary, params string[] nextSteps)
    {
        _nextStepsSummary = summary;
        _nextSteps.AddRange(nextSteps);
        return this;
    }

    public GetSubmissionDetailResultBuilder WithIdentifier(string? type, string? value)
    {
        _resolvedIdentifierType = type;
        _resolvedIdentifierValue = value;
        return this;
    }

    public GetSubmissionDetailResultBuilder WithAuditEntry(SubmissionAuditEntry entry)
    {
        _auditEntries.Add(entry);
        return this;
    }

    public GetSubmissionDetailResultBuilder WithAuditEntry(Action<SubmissionAuditEntryBuilder> configure)
    {
        var builder = new SubmissionAuditEntryBuilder()
            .WithSubmissionId(_session.SubmissionId)
            .WithSchema(_session.SchemaName, _session.SchemaVersion);
        configure(builder);
        _auditEntries.Add(builder.Build());
        return this;
    }

    public GetSubmissionDetailResult Build()
    {
        return new GetSubmissionDetailResult(
            Session: _session,
            RawSessionJson: _rawSessionJson,
            DraftItems: _draftItems.ToArray(),
            MergedDraftPayloadJson: _mergedDraftPayloadJson,
            ProcessedArtifact: _processedArtifact,
            IsReadyToFinalise: _isReadyToFinalise,
            PendingErrors: _pendingErrors.ToArray(),
            NextSteps: _nextSteps.ToArray(),
            NextStepsSummary: _nextStepsSummary,
            ResolvedIdentifierType: _resolvedIdentifierType,
            ResolvedIdentifierValue: _resolvedIdentifierValue,
            AuditEntries: _auditEntries.ToArray());
    }
}
