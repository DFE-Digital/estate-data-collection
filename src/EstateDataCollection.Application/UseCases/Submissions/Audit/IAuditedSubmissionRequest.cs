namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

/// <summary>
/// Marker contract implemented by submission use-case requests that participate in
/// ambient step tracing (<see cref="SubmissionExecutionScope"/>) and audit trail persistence.
/// </summary>
public interface IAuditedSubmissionRequest
{
    string AuditActionName { get; }

    Guid InitialSubmissionId { get; }
}
