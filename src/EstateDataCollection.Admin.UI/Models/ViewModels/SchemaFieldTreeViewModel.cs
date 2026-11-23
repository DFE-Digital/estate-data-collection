using EstateDataCollection.Application.Models.Contracts;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaFieldTreeViewModel(
    IReadOnlyList<SchemaFieldNodeViewModel> Fields,
    int Depth,
    bool ShowFieldsTable,
    bool ShowEmptyMessage,
    string ContainerCssClass,
    string HeadCssClass,
    string FieldColumnStyle)
{
    public static SchemaFieldTreeViewModel From(
        IReadOnlyList<SchemaFieldDescriptor>? fields,
        int depth = 0)
    {
        var nodes = (fields ?? [])
            .Select(f =>
            {
                var constraintsText = BuildConstraintsText(f.Constraints);
                var childrenTree = f.Children is { Count: > 0 }
                    ? From(f.Children, depth + 1)
                    : null;

                return new SchemaFieldNodeViewModel(
                    Name: f.Name,
                    Description: f.Description,
                    HasDescription: !string.IsNullOrWhiteSpace(f.Description),
                    Path: f.Path,
                    TypeDisplay: !string.IsNullOrWhiteSpace(f.Format)
                        ? $"{f.Type} ({f.Format})"
                        : f.Type,
                    RequiredTagText: f.Required ? "Yes" : "No",
                    RequiredTagCssClass: f.Required ? "govuk-tag govuk-tag--red" : "govuk-tag govuk-tag--blue",
                    HasConstraints: constraintsText is not null,
                    ConstraintsText: constraintsText,
                    HasChildren: childrenTree is not null,
                    ChildrenTree: childrenTree);
            })
            .ToList();

        return new SchemaFieldTreeViewModel(
            Fields: nodes,
            Depth: depth,
            ShowFieldsTable: nodes.Count > 0,
            ShowEmptyMessage: nodes.Count == 0 && depth == 0,
            ContainerCssClass: depth > 0 ? "dfe-field-nested" : string.Empty,
            HeadCssClass: depth > 0 ? "govuk-table__head govuk-visually-hidden" : "govuk-table__head",
            FieldColumnStyle: $"width: calc(600px - (21px * {depth}));");
    }

    private static string? BuildConstraintsText(SchemaFieldConstraints? c)
    {
        if (c is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (c.Minimum.HasValue)
        {
            parts.Add($"min: {c.Minimum}");
        }

        if (c.Maximum.HasValue)
        {
            parts.Add($"max: {c.Maximum}");
        }

        if (c.MinLength.HasValue)
        {
            parts.Add($"minLen: {c.MinLength}");
        }

        if (c.MaxLength.HasValue)
        {
            parts.Add($"maxLen: {c.MaxLength}");
        }

        if (!string.IsNullOrWhiteSpace(c.Pattern))
        {
            parts.Add($"pattern: {c.Pattern}");
        }

        if (c.EnumValues is { Count: > 0 })
        {
            parts.Add($"enum: [{string.Join(", ", c.EnumValues)}]");
        }

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }
}