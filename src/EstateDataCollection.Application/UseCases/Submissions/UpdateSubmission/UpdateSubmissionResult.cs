using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

/// <summary>
/// Result of uploading and merging a draft JSON survey payload into a submission session.
/// </summary>
/// <param name="SubmissionId">Unique identifier of the submission session.</param>
/// <param name="IsSuccessful">True when the merged draft currently passes schema, quality rule, and identifier checks.</param>
/// <param name="PendingErrors">Soft validation issues currently remaining across the merged draft payload.</param>
/// <param name="SavedAtUtc">UTC timestamp when this draft chunk was persisted.</param>
/// <param name="DraftCount">Total number of timestamped draft chunks uploaded to this session so far.</param>
/// <param name="NextSteps">Recommended caller actions based on the current draft validation state.</param>
/// <param name="Summary">Human-readable summary of the draft validation evaluation.</param>
public sealed record UpdateSubmissionResult(
    Guid SubmissionId,
    bool IsSuccessful,
    IReadOnlyList<ValidationError> PendingErrors,
    DateTimeOffset SavedAtUtc,
    int DraftCount = 1,
    IReadOnlyList<string>? NextSteps = null,
    string? Summary = null);
