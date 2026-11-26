namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SubmissionAuditItemViewModel(
    Guid EventId,
    int SequenceNumber,
    string TimestampDisplay,
    string Action,
    string OutcomeStatusText,
    string OutcomeTagCssClass,
    string HttpStatusDisplay,
    string Message,
    string ErrorCode,
    string TraceId,
    string IdentifierDisplay,
    IReadOnlyList<AuditProcessRowViewModel> Processes,
    IReadOnlyList<ValidationErrorItemViewModel> ValidationIssues,
    string RawAuditJson,
    string ActorDisplay = "")
{
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool HasErrorCode => !string.IsNullOrEmpty(ErrorCode);
    public bool HasTraceId => !string.IsNullOrEmpty(TraceId);
    public bool HasIdentifier => !string.IsNullOrEmpty(IdentifierDisplay);
    public bool HasProcesses => Processes.Count > 0;
    public bool HasValidationIssues => ValidationIssues.Count > 0;
    public bool HasActor => !string.IsNullOrEmpty(ActorDisplay);
}
