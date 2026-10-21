namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public sealed record FinaliseSubmissionResult(
    Guid SubmissionId,
    int Urn,
    string SchemaName,
    int SchemaVersion,
    string Status,
    string ProcessedBlobUri,
    DateTimeOffset FinalisedAtUtc);