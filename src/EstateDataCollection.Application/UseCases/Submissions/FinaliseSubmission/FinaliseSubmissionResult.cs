namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

/// <summary>
/// Confirmation payload returned when a submission passes strict validation and is promoted to processed storage.
/// </summary>
/// <param name="SubmissionId">Unique identifier of the finalised submission session.</param>
/// <param name="SchemaName">Name of the pinned schema.</param>
/// <param name="SchemaVersion">Pinned integer schema version.</param>
/// <param name="Status">Final lifecycle state (`Finalised`).</param>
/// <param name="ProcessedBlobUri">Storage URI or blob path where the validated submission artefact was written.</param>
/// <param name="FinalisedAtUtc">UTC timestamp when the submission was finalised.</param>
/// <param name="Identifier">Extracted establishment identifier value used for partitioning, if configured.</param>
/// <param name="IdentifierType">Configured identifier type label (e.g. `URN`, `UKPRN`).</param>
public sealed record FinaliseSubmissionResult(
    Guid SubmissionId,
    string SchemaName,
    int SchemaVersion,
    string Status,
    string ProcessedBlobUri,
    DateTimeOffset FinalisedAtUtc,
    string? Identifier = null,
    string? IdentifierType = null);
