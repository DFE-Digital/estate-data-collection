namespace EstateDataCollection.Application.UseCases.Submissions.AutoSubmission;

[Obsolete]
public sealed record AutoSubmissionRequest(
    string SchemaName,
    int? Version,
    string PayloadJson);