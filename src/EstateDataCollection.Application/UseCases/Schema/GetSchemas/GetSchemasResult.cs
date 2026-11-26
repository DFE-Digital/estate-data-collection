using EstateDataCollection.Application.Models.Contracts;

namespace EstateDataCollection.Application.UseCases.Schema.GetSchemas;

/// <summary>
/// Result returned when querying the public or admin schema catalogue.
/// </summary>
/// <param name="Schemas">Matching schema catalogue entries ordered by schema name.</param>
public sealed record GetSchemasResult(
    IReadOnlyList<SchemaCatalogueEntry> Schemas
);
