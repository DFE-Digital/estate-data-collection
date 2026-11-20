using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaVersionsViewModel(
    AdminPageMeta Page,
    SchemaCatalogueEntry? Schema,
    Error? PageError = null)
{
    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(PageError); }
    }

    public bool HasSchema
    {
        get { return Schema is not null; }
    }

    public string SchemaName
    {
        get { return Schema?.SchemaName ?? string.Empty; }
    }

    public SchemaSectionCardViewModel? SchemaCard
    {
        get
        {
            return Schema is null
                ? null
                : (_schemaCard ??= SchemaSectionCardViewModel.From(Schema, enrichedView: true));
        }
    }

    private SchemaSectionCardViewModel? _schemaCard;
}
