namespace EstateDataCollection.AzureFunctions.Models.Dtos;

public record VerifySchemaBodyDto(
    string SchemaJson,
    IReadOnlyList<string>? SampleValidFixtures,
    IReadOnlyList<string>? SampleInvalidFixtures);