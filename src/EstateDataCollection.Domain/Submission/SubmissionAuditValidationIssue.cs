using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionAuditValidationIssue(
    string PropertyPath,
    string Message,
    string? RuleId = null,
    RuleSeverity Severity = RuleSeverity.Error);
