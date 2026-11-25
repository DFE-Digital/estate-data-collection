namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record ValidationErrorItemViewModel(
    bool HasPropertyPath,
    string PropertyPath,
    string Message,
    string SeverityName = "Error",
    string SeverityTagCssClass = "govuk-tag govuk-tag--orange",
    string? EngineId = null)
{
    public bool HasEngineId => !string.IsNullOrWhiteSpace(EngineId);
}
