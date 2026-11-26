using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;

/// <summary>
/// Snapshot of a submission session's lifecycle state, draft progress, and validation readiness.
/// </summary>
/// <param name="SubmissionId">Unique identifier of the submission session.</param>
/// <param name="Status">Current lifecycle state (e.g. `Draft`, `Finalised`).</param>
/// <param name="SchemaName">Name of the pinned schema.</param>
/// <param name="SchemaVersion">Pinned integer schema version.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the session was created.</param>
/// <param name="FinalisedAtUtc">UTC timestamp when the submission was finalised, or null if still a draft.</param>
/// <param name="HasDraftPayload">True when at least one draft chunk has been uploaded.</param>
/// <param name="IsReadyToFinalise">True when a draft exists and currently passes all validation checks required to finalise.</param>
/// <param name="PendingErrors">Current schema, quality rule, and identifier validation errors blocking finalisation.</param>
/// <param name="Identifier">Extracted establishment identifier value (e.g. school URN), when available.</param>
/// <param name="IdentifierType">Configured identifier type label (e.g. `URN`, `UKPRN`, or `None`).</param>
/// <param name="DraftCount">Total number of timestamped draft chunks uploaded to this session.</param>
/// <param name="LastDraftAtUtc">UTC timestamp of the most recent draft chunk upload.</param>
/// <param name="NextSteps">Recommended caller actions based on the current submission state.</param>
/// <param name="Summary">Human-readable summary of the submission status and readiness.</param>
public sealed record GetSubmissionStatusResult(
    Guid SubmissionId,
    string Status,
    string SchemaName,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinalisedAtUtc,
    bool HasDraftPayload,
    bool IsReadyToFinalise,
    IReadOnlyList<ValidationError> PendingErrors,
    string? Identifier = null,
    string? IdentifierType = null,
    int DraftCount = 0,
    DateTimeOffset? LastDraftAtUtc = null,
    IReadOnlyList<string>? NextSteps = null,
    string? Summary = null);
