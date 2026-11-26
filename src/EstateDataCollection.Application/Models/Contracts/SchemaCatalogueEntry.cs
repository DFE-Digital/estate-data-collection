namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Grouping of all visible versions for a named schema in the catalogue.
/// </summary>
/// <param name="SchemaName">Unique schema identifier (e.g. `condition-survey`).</param>
/// <param name="ActiveVersion">Currently active version number accepting traffic, or null if none is active.</param>
/// <param name="Versions">Ordered list of visible schema versions matching the query filters.</param>
/// <param name="ActiveVersionFields">Extracted field descriptors for the active version when requested (`includeFields=true`).</param>
public sealed record SchemaCatalogueEntry(
    string SchemaName,
    int? ActiveVersion,
    IReadOnlyList<SchemaVersionSummary> Versions,
    IReadOnlyList<SchemaFieldDescriptor>? ActiveVersionFields = null
);
