using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

public readonly record struct ProcessStepDetails(
    SubmissionProcessOutcome Outcome = SubmissionProcessOutcome.Succeeded,
    int? ItemCount = null,
    string? Summary = null,
    string? EngineOverride = null,
    string? ErrorCode = null,
    IReadOnlyList<SubmissionAuditValidationIssue>? Errors = null);
