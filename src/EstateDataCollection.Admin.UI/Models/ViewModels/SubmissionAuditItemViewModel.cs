namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SubmissionAuditItemViewModel(
    Guid EventId,
    int SequenceNumber,
    string TimestampDisplay,
    string Action,
    string OutcomeStatusText,
    string OutcomeTagCssClass,
    string HttpStatusDisplay,
    bool HasMessage,
    string Message,
    bool HasErrorCode,
    string ErrorCode,
    bool HasTraceId,
    string TraceId,
    bool HasIdentifier,
    string IdentifierDisplay,
    IReadOnlyList<AuditProcessRowViewModel> Processes,
    bool HasProcesses,
    IReadOnlyList<ValidationErrorItemViewModel> ValidationIssues,
    bool HasValidationIssues,
    string RawAuditJson);