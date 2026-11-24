using EstateDataCollection.Application.UseCases.Submissions.Audit;

namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public sealed record FinaliseSubmissionRequest(
    Guid SubmissionId) : IAuditedSubmissionRequest
{
    string IAuditedSubmissionRequest.AuditActionName => "FinaliseSubmission";
    Guid IAuditedSubmissionRequest.InitialSubmissionId => SubmissionId;
}
