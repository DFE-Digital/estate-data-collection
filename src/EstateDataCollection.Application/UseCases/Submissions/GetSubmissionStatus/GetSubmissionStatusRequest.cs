using EstateDataCollection.Application.UseCases.Submissions.Audit;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;

public sealed record GetSubmissionStatusRequest(
    Guid SubmissionId,
    string ActionName = "CheckSubmissionStatus") : IAuditedSubmissionRequest
{
    string IAuditedSubmissionRequest.AuditActionName =>
        string.IsNullOrWhiteSpace(ActionName) ? "CheckSubmissionStatus" : ActionName;

    Guid IAuditedSubmissionRequest.InitialSubmissionId => SubmissionId;
}
