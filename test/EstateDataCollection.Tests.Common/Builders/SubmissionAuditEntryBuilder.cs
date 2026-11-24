using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SubmissionAuditEntryBuilder
{
    private Guid _eventId = Guid.CreateVersion7();
    private DateTimeOffset _timestampUtc = DateTimeOffset.UtcNow;
    private Guid _submissionId = Guid.NewGuid();
    private string _action = "CheckSubmission";
    private string? _traceId;
    private string? _actor;
    private string? _schemaName = "edc_payload";
    private int? _schemaVersion = 1;
    private SubmissionAuditOutcome _outcome = new() { Status = "Succeeded", HttpStatus = 200 };
    private SubmissionAuditIdentifier? _identifier;
    private readonly List<SubmissionProcessExecution> _processes = [];
    private Dictionary<string, string>? _details;

    public SubmissionAuditEntryBuilder WithEventId(Guid eventId)
    {
        _eventId = eventId;
        return this;
    }

    public SubmissionAuditEntryBuilder WithTimestamp(DateTimeOffset timestampUtc)
    {
        _timestampUtc = timestampUtc;
        return this;
    }

    public SubmissionAuditEntryBuilder WithSubmissionId(Guid submissionId)
    {
        _submissionId = submissionId;
        return this;
    }

    public SubmissionAuditEntryBuilder WithAction(string action)
    {
        _action = action;
        return this;
    }

    public SubmissionAuditEntryBuilder WithTraceId(string? traceId)
    {
        _traceId = traceId;
        return this;
    }

    public SubmissionAuditEntryBuilder WithActor(string? actor)
    {
        _actor = actor;
        return this;
    }

    public SubmissionAuditEntryBuilder WithSchema(string? schemaName, int? schemaVersion = null)
    {
        _schemaName = schemaName;
        _schemaVersion = schemaVersion;
        return this;
    }

    public SubmissionAuditEntryBuilder WithOutcome(
        string status,
        int httpStatus = 200,
        string? errorCode = null,
        string? message = null,
        IReadOnlyList<SubmissionAuditValidationIssue>? validationErrors = null)
    {
        _outcome = new SubmissionAuditOutcome
        {
            Status = status,
            HttpStatus = httpStatus,
            ErrorCode = errorCode,
            Message = message,
            ValidationErrorCount = validationErrors?.Count ?? 0,
            ValidationErrors = validationErrors
        };
        return this;
    }

    public SubmissionAuditEntryBuilder WithIdentifier(string? type, string? value, int? urn = null)
    {
        _identifier = new SubmissionAuditIdentifier(type, value, urn);
        return this;
    }

    public SubmissionAuditEntryBuilder WithProcess(SubmissionProcessExecution process)
    {
        _processes.Add(process);
        return this;
    }

    public SubmissionAuditEntryBuilder WithProcess(
        SubmissionProcessDescriptor descriptor,
        Action<SubmissionProcessExecutionBuilder>? configure = null)
    {
        var builder = new SubmissionProcessExecutionBuilder().For(descriptor);
        configure?.Invoke(builder);
        _processes.Add(builder.Build());
        return this;
    }

    public SubmissionAuditEntryBuilder WithDetail(string key, string value)
    {
        _details ??= new Dictionary<string, string>(StringComparer.Ordinal);
        _details[key] = value;
        return this;
    }

    public SubmissionAuditEntry Build()
    {
        return new SubmissionAuditEntry
        {
            EventId = _eventId,
            TimestampUtc = _timestampUtc,
            SubmissionId = _submissionId,
            Action = _action,
            TraceId = _traceId,
            Actor = _actor,
            SchemaName = _schemaName,
            SchemaVersion = _schemaVersion,
            Outcome = _outcome,
            Identifier = _identifier,
            ProcessesConsumed = _processes.ToArray(),
            Details = _details
        };
    }
}
