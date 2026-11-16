namespace EstateDataCollection.Application.UseCases.Schema.VerifySchema;

public sealed record VerifySchemaRequest(
    string? SchemaJson,
    IReadOnlyList<string> SampleValidFixtures,
    IReadOnlyList<string> SampleInvalidFixtures,
    string? RulesJson = null,
    string? OdcsYaml = null,
    string? FileName = null
);
