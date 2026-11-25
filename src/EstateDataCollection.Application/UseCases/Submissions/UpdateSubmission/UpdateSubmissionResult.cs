using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public sealed record UpdateSubmissionResult(
    Guid SubmissionId,
    bool IsSuccessful,
    IReadOnlyList<ValidationError> PendingErrors,
    DateTimeOffset SavedAtUtc,
    int DraftCount = 1,
    IReadOnlyList<string>? NextSteps = null,
    string? Summary = null,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null);
