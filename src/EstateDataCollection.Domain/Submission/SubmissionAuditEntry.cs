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