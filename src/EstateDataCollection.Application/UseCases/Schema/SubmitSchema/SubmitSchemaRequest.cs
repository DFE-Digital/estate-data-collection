namespace EstateDataCollection.Application.UseCases.Schema.SubmitSchema;

public sealed record SubmitSchemaRequest(
    string SchemaName,
    int Version,
    string SchemaJson,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc = null,
    IReadOnlyList<string>? SampleValidFixtures = null,
    IReadOnlyList<string>? SampleInvalidFixtures = null
);