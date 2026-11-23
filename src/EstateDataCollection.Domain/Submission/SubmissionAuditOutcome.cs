namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionAuditOutcome
{
    public string Status { get; init; } = string.Empty;
    public int HttpStatus { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public int ValidationErrorCount { get; init; }
    public IReadOnlyList<SubmissionAuditValidationIssue>? ValidationErrors { get; init; }
}