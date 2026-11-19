namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionAuditEntry
{
    public required Guid EventId { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required Guid SubmissionId { get; init; }
    public required string Action { get; init; }
    public string? TraceId { get; init; }
    public string? Actor { get; init; }
    public string? SchemaName { get; init; }
    public int? SchemaVersion { get; init; }
    public SubmissionAuditOutcome Outcome { get; init; } = new();
    public SubmissionAuditIdentifier? Identifier { get; init; }
    public IReadOnlyList<SubmissionProcessExecution> ProcessesConsumed { get; init; } = [];
    public IReadOnlyDictionary<string, string>? Details { get; init; }
}

public sealed record SubmissionAuditOutcome
{
    public string Status { get; init; } = string.Empty;
    public int HttpStatus { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public int ValidationErrorCount { get; init; }
    public IReadOnlyList<SubmissionAuditValidationIssue>? ValidationErrors { get; init; }
}

public sealed record SubmissionAuditIdentifier(string? Type, string? Value, int? Urn = null);

public sealed record SubmissionProcessExecution
{
    public required string ProcessName { get; init; }
    public string? Engine { get; init; }
    public required string Outcome { get; init; }
    public long DurationMs { get; init; }
    public int? ItemCount { get; init; }
    public string? Summary { get; init; }
}

public sealed record SubmissionAuditValidationIssue(string PropertyPath, string Message, string? RuleId = null);
