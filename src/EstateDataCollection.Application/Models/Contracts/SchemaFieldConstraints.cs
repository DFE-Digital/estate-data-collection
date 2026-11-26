namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Validation constraints declared on a JSON Schema field.
/// </summary>
/// <param name="Minimum">Inclusive minimum numeric bound.</param>
/// <param name="Maximum">Inclusive maximum numeric bound.</param>
/// <param name="MinLength">Minimum string length.</param>
/// <param name="MaxLength">Maximum string length.</param>
/// <param name="Pattern">ECMA-262 regular expression pattern the value must match.</param>
/// <param name="EnumValues">Permitted discrete string or literal values.</param>
public sealed record SchemaFieldConstraints(
    decimal? Minimum = null,
    decimal? Maximum = null,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    IReadOnlyList<string>? EnumValues = null
);
