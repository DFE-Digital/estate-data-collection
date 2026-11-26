namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Represents a single field or property extracted from a JSON Schema contract.
/// </summary>
/// <param name="Name">Property name within its parent object.</param>
/// <param name="Path">JSON Pointer or hierarchical path to this field (e.g. `/school/urn`).</param>
/// <param name="Type">JSON Schema primitive or composite type (`string`, `integer`, `object`, `array`, etc.).</param>
/// <param name="Required">True when the field is marked required by its parent schema object.</param>
/// <param name="Title">Optional human-readable field label from the schema `title` keyword.</param>
/// <param name="Description">Optional guidance from the schema `description` keyword.</param>
/// <param name="Format">Optional JSON Schema string format hint (e.g. `date`, `uuid`, `email`).</param>
/// <param name="Constraints">Value and length constraints extracted for this field.</param>
/// <param name="Children">Nested child field descriptors for `object` or `array` properties.</param>
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
