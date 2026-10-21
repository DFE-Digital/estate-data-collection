namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public sealed record FinaliseSubmissionRequest(
    Guid SubmissionId);