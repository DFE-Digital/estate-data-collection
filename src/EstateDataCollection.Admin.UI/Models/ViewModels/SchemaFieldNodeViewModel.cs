namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaFieldNodeViewModel(
    string Name,
    string? Description,
    bool HasDescription,
    string Path,
    string TypeDisplay,
    string RequiredTagText,
    string RequiredTagCssClass,
    bool HasConstraints,
    string? ConstraintsText,
    bool HasChildren,
    SchemaFieldTreeViewModel? ChildrenTree);