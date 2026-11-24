namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Grouping of all versions for a named schema in the catalogue.
/// </summary>
public sealed record SchemaCatalogueEntry(
    string SchemaName,
    int? ActiveVersion,
    IReadOnlyList<SchemaVersionSummary> Versions,
    IReadOnlyList<SchemaFieldDescriptor>? ActiveVersionFields = null
);