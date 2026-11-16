using EstateDataCollection.Common;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Represents a single field/property extracted from a JSON Schema contract.
/// </summary>
public sealed record SchemaFieldDescriptor(
    string Name,
    string Path,
    string Type,
    bool Required,
    string? Title = null,
    string? Description = null,
    string? Format = null,
    SchemaFieldConstraints? Constraints = null,
    IReadOnlyList<SchemaFieldDescriptor>? Children = null
);

/// <summary>
/// Validation constraints declared on a JSON Schema field.
/// </summary>
public sealed record SchemaFieldConstraints(
    decimal? Minimum = null,
    decimal? Maximum = null,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    IReadOnlyList<string>? EnumValues = null
);

/// <summary>
/// Summary of a quality / business rule attached to a schema version.
/// </summary>
public sealed record SchemaQualityRuleSummary(
    string Id,
    string? Dimension,
    string? Severity,
    string? TargetPath,
    string? Engine,
    string ErrorMessage,
    string? Description = null
);

/// <summary>
/// Summary of a single schema version in the catalogue.
/// </summary>
public sealed record SchemaVersionSummary(
    string SchemaName,
    int Version,
    Node<int> State,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool IsActiveNow,
    bool CanAcceptTraffic,
    string Sha256Hash,
    DateTimeOffset CreatedAtUtc,
    SchemaIdentifierConfig? IdentifierConfig = null
);

/// <summary>
/// Grouping of all versions for a named schema in the catalogue.
/// </summary>
public sealed record SchemaCatalogueEntry(
    string SchemaName,
    int? ActiveVersion,
    IReadOnlyList<SchemaVersionSummary> Versions,
    IReadOnlyList<SchemaFieldDescriptor>? ActiveVersionFields = null
);

/// <summary>
/// Full details of a specific schema version including extracted field definitions and quality rules.
/// </summary>
public sealed record SchemaVersionDetail(
    string SchemaName,
    int Version,
    Node<int> State,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool IsActiveNow,
    bool CanAcceptTraffic,
    string Sha256Hash,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<SchemaFieldDescriptor> Fields,
    IReadOnlyList<SchemaQualityRuleSummary> QualityRules,
    string? RawJsonSchema = null,
    SchemaIdentifierConfig? IdentifierConfig = null
);
