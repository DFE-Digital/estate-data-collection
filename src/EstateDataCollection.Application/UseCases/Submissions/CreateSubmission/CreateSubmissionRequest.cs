using EstateDataCollection.Application.UseCases.Submissions.Audit;

namespace EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;

public sealed record CreateSubmissionRequest(
    string SchemaName,
    int? Version) : IAuditedSubmissionRequest
{
    string IAuditedSubmissionRequest.AuditActionName => "CreateSubmission";
    Guid IAuditedSubmissionRequest.InitialSubmissionId => Guid.Empty;
}
