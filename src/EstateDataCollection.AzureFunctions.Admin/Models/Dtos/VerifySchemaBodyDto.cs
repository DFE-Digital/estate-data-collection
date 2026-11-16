namespace EstateDataCollection.AzureFunctions.Admin.Models.Dtos;

public record VerifySchemaBodyDto(
    string? SchemaJson = null,
    IReadOnlyList<string>? SampleValidFixtures = null,
    IReadOnlyList<string>? SampleInvalidFixtures = null,
    string? LogicJson = null,
    string? RulesJson = null,
    string? OdcsYaml = null,
    string? FileName = null)
{
    public string? EffectiveRulesJson => !string.IsNullOrWhiteSpace(RulesJson) ? RulesJson : LogicJson;
}
