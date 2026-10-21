namespace EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;

public sealed record CreateSubmissionResult(
    Guid SubmissionId,
    string SchemaName,
    int Version,
    DateTimeOffset CreatedAtUtc);