using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaVersionsViewModel(
    AdminPageMeta Page,
    SchemaCatalogueEntry? Schema,
    Error? PageError = null)
{
    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(PageError);

    public bool HasSchema => Schema is not null;

    public string SchemaName => Schema?.SchemaName ?? string.Empty;

    public SchemaSectionCardViewModel? SchemaCard =>
        Schema is null
            ? null
            : (_schemaCard ??= SchemaSectionCardViewModel.From(Schema, enrichedView: true));
    private SchemaSectionCardViewModel? _schemaCard;
}
