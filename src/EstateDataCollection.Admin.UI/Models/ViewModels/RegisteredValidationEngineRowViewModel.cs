namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record RegisteredValidationEngineRowViewModel(
    string EngineId,
    string DisplayName,
    string KindLabel,
    string Description,
    string ApplicabilityText,
    string ApplicabilityTagCssClass);