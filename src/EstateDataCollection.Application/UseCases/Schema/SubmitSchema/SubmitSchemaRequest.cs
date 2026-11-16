using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.SubmitSchema;

public sealed record SubmitSchemaRequest(
    string SchemaName,
    int Version,
    string SchemaJson,
    IReadOnlyList<string>? SampleValidFixtures = null,
    IReadOnlyList<string>? SampleInvalidFixtures = null,
    string? RulesJson = null,
    string? OdcsYaml = null,
    string? FileName = null,
    SchemaIdentifierConfig? IdentifierConfig = null
);
