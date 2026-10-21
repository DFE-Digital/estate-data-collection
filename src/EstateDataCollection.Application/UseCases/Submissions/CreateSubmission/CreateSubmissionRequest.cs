namespace EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;

public sealed record CreateSubmissionRequest(
    string SchemaName, int? Version);