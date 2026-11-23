namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record AuditProcessRowViewModel(
    string ProcessName,
    string EngineDisplay,
    string OutcomeText,
    string OutcomeTagCssClass,
    string DurationDisplay,
    string ItemCountDisplay,
    string SummaryDisplay);