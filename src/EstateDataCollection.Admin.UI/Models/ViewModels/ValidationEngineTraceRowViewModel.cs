namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record ValidationEngineTraceRowViewModel(
    string EngineId,
    string DisplayName,
    string KindLabel,
    string Description,
    string OutcomeText,
    string OutcomeTagCssClass,
    string ItemsEvaluatedDisplay,
    string ViolationCountDisplay,
    string DurationDisplay,
    string SummaryDisplay);