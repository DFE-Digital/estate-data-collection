namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionProcessExecution
{
    public required string ProcessName { get; init; }
    public string? Engine { get; init; }
    public required SubmissionProcessOutcome Outcome { get; init; }
    public long DurationMs { get; init; }
    public int? ItemCount { get; init; }
    public string? Summary { get; init; }
}