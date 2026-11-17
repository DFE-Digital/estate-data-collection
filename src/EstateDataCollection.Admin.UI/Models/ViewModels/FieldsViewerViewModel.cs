using EstateDataCollection.Application.Models.Contracts;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record FieldsViewerViewModel(
    int TopLevelFieldCount,
    SchemaFieldTreeViewModel Tree)
{
    public static FieldsViewerViewModel From(IReadOnlyList<SchemaFieldDescriptor>? fields)
    {
        var list = fields ?? [];
        return new FieldsViewerViewModel(
            TopLevelFieldCount: list.Count,
            Tree: SchemaFieldTreeViewModel.From(list));
    }
}
