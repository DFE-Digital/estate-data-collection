namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;

public sealed record GetSubmissionStatusRequest(
    Guid SubmissionId,
    string ActionName = "CheckSubmissionStatus");
