namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record FixtureEditorFieldViewModel(
    string EditorText,
    bool HasError,
    string? ErrorMessage,
    string FormGroupCssClass,
    string TextareaCssClass,
    string AriaDescribedBy);