namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record QualityRuleCardViewModel(
    string Id,
    string Description,
    string Engine,
    string Severity,
    string ErrorMessage);