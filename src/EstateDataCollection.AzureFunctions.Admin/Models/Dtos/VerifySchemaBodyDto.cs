using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.AzureFunctions.Admin.Models.Dtos;

public record VerifySchemaBodyDto(
    string? SchemaJson = null,
    IReadOnlyList<string>? SampleValidFixtures = null,
    IReadOnlyList<string>? SampleInvalidFixtures = null,
    string? LogicJson = null,
    string? RulesJson = null,
    string? OdcsYaml = null,
    string? FileName = null,
    SchemaIdentifierConfig? IdentifierConfig = null)
{
    public string? EffectiveRulesJson
    {
        get { return !string.IsNullOrWhiteSpace(RulesJson) ? RulesJson : LogicJson; }
    }
}
