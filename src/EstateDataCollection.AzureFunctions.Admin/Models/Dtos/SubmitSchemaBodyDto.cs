using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.AzureFunctions.Admin.Models.Dtos;

public record SubmitSchemaBodyDto(
    string? SchemaJson = null,
    IReadOnlyList<string>? SampleValidFixtures = null,
    IReadOnlyList<string>? SampleInvalidFixtures = null,
    string? LogicJson = null,
    string? RulesJson = null,
    string? OdcsYaml = null,
    string? FileName = null,
    SchemaIdentifierConfig? IdentifierConfig = null)
{
    public string? EffectiveRulesJson => !string.IsNullOrWhiteSpace(RulesJson) ? RulesJson : LogicJson;
}
