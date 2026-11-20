using EstateDataCollection.Application.Models.Contracts;

namespace EstateDataCollection.Application.UseCases.Schema.GetSchemas;

public sealed record GetSchemasResult(
    IReadOnlyList<SchemaCatalogueEntry> Schemas
);
