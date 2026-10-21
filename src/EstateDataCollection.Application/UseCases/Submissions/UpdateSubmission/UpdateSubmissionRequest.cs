namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public sealed record UpdateSubmissionRequest(
    Guid SubmissionId,
    string Payload);