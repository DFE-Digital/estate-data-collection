namespace EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;

/// <summary>
/// Confirmation payload returned when a new draft submission session is created.
/// </summary>
/// <param name="SubmissionId">Unique identifier assigned to the submission session.</param>
/// <param name="SchemaName">Name of the schema this submission is pinned to.</param>
/// <param name="Version">Pinned integer schema version that drafts and finalisation will validate against.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the submission session was created.</param>
public sealed record CreateSubmissionResult(
    Guid SubmissionId,
    string SchemaName,
    int Version,
    DateTimeOffset CreatedAtUtc);
