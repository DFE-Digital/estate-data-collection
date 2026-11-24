namespace EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;

/// <summary>
/// Requests execution of stored regression tests (JSON Schema, quality/business logic rules,
/// identifier alignment, and valid/invalid payload fixtures) against one or all stored schemas.
/// </summary>
public sealed record RunSchemaTestsRequest(
    string? SchemaName = null,
    int? Version = null,
    bool IncludeRetired = false);
