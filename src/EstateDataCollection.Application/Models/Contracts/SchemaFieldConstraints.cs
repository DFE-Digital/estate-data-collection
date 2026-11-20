namespace EstateDataCollection.Application.Models.Contracts;

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