using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Validators.Logic;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class QualityRulesBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly List<object> _rawRules = [];

    public QualityRulesBuilder AddJsonLogicTimestampRule(string id = "RULE-JL-TIMESTAMP")
    {
        _rawRules.Add(new
        {
            id,
            description = "Updated timestamp must be greater than or equal to created timestamp",
            engine = "jsonlogic",
            path = "survey",
            message = "updated_at >= created_at",
            implementation = JsonNode.Parse("""{ ">=": [ { "var": "survey.updated_at" }, { "var": "survey.created_at" } ] }""")
        });
        return this;
    }

    public QualityRulesBuilder AddCelRule(
        string id,
        string path,
        string description,
        string expression,
        string? message = null)
    {
        _rawRules.Add(new
        {
            id,
            type = "custom",
            engine = "cel",
            path,
            description,
            message = message ?? description,
            implementation = expression
        });
        return this;
    }

    public QualityRulesBuilder AddCustomRule(
        string id,
        string engine,
        string path,
        string description,
        object implementation,
        string? message = null)
    {
        _rawRules.Add(new
        {
            id,
            type = "custom",
            engine,
            path,
            description,
            message = message ?? description,
            implementation
        });
        return this;
    }

    public string BuildJson() => JsonSerializer.Serialize(_rawRules, JsonOptions);

    public IReadOnlyList<BusinessRule> BuildList() =>
        JsonSerializer.Deserialize<List<BusinessRule>>(BuildJson(), JsonOptions) ?? [];
}
