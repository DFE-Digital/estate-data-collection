namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record IdentifierPropertySelectOptionViewModel(
    string Pointer,
    string DerivedType,
    string RequiredAttributeValue,
    bool IsSelected,
    string DisplayText);