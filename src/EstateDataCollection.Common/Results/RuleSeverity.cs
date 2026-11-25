using System.Text.Json;
using System.Text.Json.Serialization;

namespace EstateDataCollection.Common.Results;

/// <summary>
/// Severity tier for quality rules and validation findings.
/// Lower numeric values represent higher severity so threshold gating can use
/// <c>severity &lt;= FailOnMinimumSeverity</c>.
/// </summary>
[JsonConverter(typeof(RuleSeverityJsonConverter))]
public enum RuleSeverity
{
    Critical = 0,
    Error = 1,
    Warning = 2,
    Info = 3
}

public static class RuleSeverityExtensions
{
    public static string ToKeyString(this RuleSeverity severity)
    {
        return severity switch
        {
            RuleSeverity.Critical => "critical",
            RuleSeverity.Error => "error",
            RuleSeverity.Warning => "warning",
            RuleSeverity.Info => "info",
            _ => "error"
        };
    }

    public static bool IsAtOrAbove(this RuleSeverity severity, RuleSeverity threshold)
    {
        return (int)severity <= (int)threshold;
    }

    public static bool TryParse(string? raw, out RuleSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            severity = RuleSeverity.Error;
            return false;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "critical":
            case "crit":
            case "blocker":
            case "fatal":
                severity = RuleSeverity.Critical;
                return true;

            case "error":
            case "err":
                severity = RuleSeverity.Error;
                return true;

            case "warning":
            case "warn":
                severity = RuleSeverity.Warning;
                return true;

            case "info":
            case "information":
            case "advisory":
                severity = RuleSeverity.Info;
                return true;

            default:
                if (int.TryParse(raw.Trim(), out var numeric) && Enum.IsDefined(typeof(RuleSeverity), numeric))
                {
                    severity = (RuleSeverity)numeric;
                    return true;
                }

                severity = RuleSeverity.Error;
                return false;
        }
    }

    public static RuleSeverity ParseOrDefault(string? raw, RuleSeverity fallback = RuleSeverity.Error)
    {
        return TryParse(raw, out var parsed) ? parsed : fallback;
    }
}

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
