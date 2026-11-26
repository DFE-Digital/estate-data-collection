using System.Text.Json;
using System.Text.Json.Serialization;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Common.Converters;

public sealed class RuleSeverityJsonConverter : JsonConverter<RuleSeverity>
{
    public override RuleSeverity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            if (RuleSeverityExtensions.TryParse(raw, out var parsed))
            {
                return parsed;
            }

            throw new JsonException($"Invalid RuleSeverity value '{raw}'. Allowed values: critical, error, warning, info.");
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var num) && Enum.IsDefined(typeof(RuleSeverity), num))
        {
            return (RuleSeverity)num;
        }

        throw new JsonException($"Unexpected JSON token '{reader.TokenType}' when parsing RuleSeverity.");
    }

    public override void Write(Utf8JsonWriter writer, RuleSeverity value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToKeyString());
    }

    public override RuleSeverity ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (RuleSeverityExtensions.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        throw new JsonException($"Invalid RuleSeverity property name '{raw}'.");
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, RuleSeverity value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToKeyString());
    }
}