using EstateDataCollection.Application.UseCases.Submissions.Audit;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public sealed record UpdateSubmissionRequest(
    Guid SubmissionId,
    string Payload) : IAuditedSubmissionRequest
{
    string IAuditedSubmissionRequest.AuditActionName => "UpdateSubmission";
    Guid IAuditedSubmissionRequest.InitialSubmissionId => SubmissionId;
}
