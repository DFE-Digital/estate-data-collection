namespace EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;

/// <summary>
/// Replaces and re-verifies the stored valid and invalid regression test fixtures
/// (.valid.json / .invalid.json) for an existing schema version without mutating the schema contract.
/// </summary>
public sealed record UpdateSchemaTestsRequest(
    string SchemaName,
    int Version,
    IReadOnlyList<string>? ValidFixtures = null,
    IReadOnlyList<string>? InvalidFixtures = null);
