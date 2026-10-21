namespace EstateDataCollection.Application.UseCases.Submissions.AutoSubmission;

public sealed record AutoSubmissionResult(
    Guid SubmissionId,
    int Urn,
    string SchemaName,
    int Version,
    string PayloadHash,
    DateTimeOffset SubmittedAtUtc
);