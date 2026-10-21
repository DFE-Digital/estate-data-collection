namespace EstateDataCollection.AzureFunctions.Models.Dtos;

public record SubmitSchemaBodyDto(
    string SchemaJson,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc = null,
    IReadOnlyList<string>? SampleValidFixtures = null,
    IReadOnlyList<string>? SampleInvalidFixtures = null);