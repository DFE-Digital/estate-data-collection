namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record AuditProcessRowViewModel(
    string ProcessName,
    string EngineDisplay,
    string OutcomeText,
    string OutcomeTagCssClass,
    string DurationDisplay,
    string ItemCountDisplay,
    string SummaryDisplay,
    string? ErrorCode = null,
    IReadOnlyList<ValidationErrorItemViewModel>? Errors = null)
{
    public bool HasErrorCode => !string.IsNullOrWhiteSpace(ErrorCode);

    public bool HasErrors => Errors is { Count: > 0 };
}
