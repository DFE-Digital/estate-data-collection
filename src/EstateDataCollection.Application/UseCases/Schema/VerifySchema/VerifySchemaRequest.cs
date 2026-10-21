namespace EstateDataCollection.Application.UseCases.Schema.VerifySchema;

public record VerifySchemaRequest(
    string SchemaJson,
    IReadOnlyList<string> SampleValidFixtures,
    IReadOnlyList<string> SampleInvalidFixtures
);