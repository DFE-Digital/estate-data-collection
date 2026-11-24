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