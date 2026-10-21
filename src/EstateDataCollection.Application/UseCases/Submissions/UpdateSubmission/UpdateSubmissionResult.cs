using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public sealed record UpdateSubmissionResult(
    Guid SubmissionId,
    bool IsSuccessful,
    IReadOnlyList<ValidationError> PendingErrors,
    DateTimeOffset SavedAtUtc);