namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record ValidationErrorItemViewModel(
    bool HasPropertyPath,
    string PropertyPath,
    string Message);