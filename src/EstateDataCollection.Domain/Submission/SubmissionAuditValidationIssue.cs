namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionAuditValidationIssue(string PropertyPath, string Message, string? RuleId = null);