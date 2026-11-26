namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record FixtureEditorFieldViewModel(
    string EditorText,
    string? ErrorMessage,
    string FormGroupCssClass,
    string TextareaCssClass,
    string AriaDescribedBy)
{
    public bool HasError => string.IsNullOrWhiteSpace(ErrorMessage);
}