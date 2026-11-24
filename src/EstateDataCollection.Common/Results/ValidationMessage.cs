using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EstateDataCollection.Common.Results;

public static class ValidationSources
{
    public const string Schema = "Schema";
    public const string Rule = "Rule";
    public const string Identifier = "Identifier";
    public const string Json = "JSON";
    public const string Odcs = "ODCS";
    public const string Verification = "Verification";
}

[JsonConverter(typeof(ValidationMessageJsonConverter))]
public sealed partial record ValidationMessage
{
    public ValidationMessage(
        Type? engineType,
        string? identifier,
        string message,
        string? source = null,
        string? path = null,
        string? ruleId = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        EngineType = engineType;
        Identifier = identifier;
        Message = message ?? string.Empty;
        Source = string.IsNullOrWhiteSpace(source) ? engineType?.Name : source;
        Path = path;
        RuleId = ruleId;
        Metadata = metadata;
    }

    [JsonIgnore]
    public Type? EngineType { get; init; }

    public string? Identifier { get; init; }
    public string Message { get; init; }
    public string? Source { get; init; }
    public string? Path { get; init; }
    public string? RuleId { get; init; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    public static ValidationMessage For(
        string? source,
        string message,
        string? identifier = null,
        string? path = null,
        string? ruleId = null,
        Type? engineType = null,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(
            engineType: engineType,
            identifier: identifier,
            message: message,
            source: source,
            path: path,
            ruleId: ruleId,
            metadata: metadata);

    public string FormatDisplay()
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            return string.IsNullOrWhiteSpace(Path)
                ? Message
                : $"{Path}: {Message}";
        }

        var tag = string.IsNullOrWhiteSpace(RuleId)
            ? $"[{Source}]"
            : $"[{Source}:{RuleId}]";

        var location = string.IsNullOrWhiteSpace(Path)
            ? string.Empty
            : $"{Path}: ";

        return $"{tag} {location}{Message}";
    }

    public bool MatchesPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(RuleId) && RuleId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(Source) && Source.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(Path) && Path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(Identifier) && Identifier.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            || Message.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            || FormatDisplay().Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public static ValidationMessage Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return new ValidationMessage(null, null, string.Empty);
        }

        var tagMatch = LeadingSourceTagRegex().Match(raw);
        if (!tagMatch.Success)
        {
            return new ValidationMessage(null, null, raw);
        }

        var source = tagMatch.Groups["source"].Value;
        var ruleId = tagMatch.Groups["ruleId"].Success ? tagMatch.Groups["ruleId"].Value : null;
        var remainder = tagMatch.Groups["rest"].Value;

        string? path = null;
        var message = remainder;

        var pathMatch = LeadingPathRegex().Match(remainder);
        if (pathMatch.Success)
        {
            path = pathMatch.Groups["path"].Value;
            message = pathMatch.Groups["msg"].Value;
        }

        return For(source, message, path: path, ruleId: ruleId);
    }

    public override string ToString() => FormatDisplay();

    public bool Equals(ValidationMessage? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return EngineType == other.EngineType
            && string.Equals(Identifier, other.Identifier, StringComparison.Ordinal)
            && string.Equals(Message, other.Message, StringComparison.Ordinal)
            && string.Equals(Source, other.Source, StringComparison.Ordinal)
            && string.Equals(Path, other.Path, StringComparison.Ordinal)
            && string.Equals(RuleId, other.RuleId, StringComparison.Ordinal)
            && MetadataEquals(Metadata, other.Metadata);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EngineType);
        hash.Add(Identifier, StringComparer.Ordinal);
        hash.Add(Message, StringComparer.Ordinal);
        hash.Add(Source, StringComparer.Ordinal);
        hash.Add(Path, StringComparer.Ordinal);
        hash.Add(RuleId, StringComparer.Ordinal);
        hash.Add(Metadata?.Count ?? 0);
        return hash.ToHashCode();
    }

    private static bool MetadataEquals(
        IReadOnlyDictionary<string, object?>? left,
        IReadOnlyDictionary<string, object?>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, leftVal) in left)
        {
            if (!right.TryGetValue(key, out var rightVal) || !Equals(leftVal, rightVal))
            {
                return false;
            }
        }

        return true;
    }

    public static implicit operator ValidationMessage(string message) => Parse(message);

    [GeneratedRegex(@"^\[(?<source>[^:\]\s]+)(?::(?<ruleId>[^\]]+))?\]\s*(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingSourceTagRegex();

    [GeneratedRegex(@"^(?<path>/[^\s:]*):\s+(?<msg>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingPathRegex();
}

public sealed class ValidationMessageJsonConverter : JsonConverter<ValidationMessage>
{
    public override ValidationMessage? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return ValidationMessage.Parse(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Unexpected token {reader.TokenType} when reading {nameof(ValidationMessage)}.");
        }

        string? source = null;
        string? identifier = null;
        string? path = null;
        string? ruleId = null;
        string? message = null;
        Dictionary<string, object?>? metadata = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return ValidationMessage.For(
                    source: source,
                    message: message ?? string.Empty,
                    identifier: identifier,
                    path: path,
                    ruleId: ruleId,
                    metadata: metadata);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected property name in {nameof(ValidationMessage)}.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, nameof(ValidationMessage.Source), StringComparison.OrdinalIgnoreCase))
            {
                source = reader.GetString();
            }
            else if (string.Equals(propertyName, nameof(ValidationMessage.Identifier), StringComparison.OrdinalIgnoreCase))
            {
                identifier = reader.GetString();
            }
            else if (string.Equals(propertyName, nameof(ValidationMessage.Path), StringComparison.OrdinalIgnoreCase))
            {
                path = reader.GetString();
            }
            else if (string.Equals(propertyName, nameof(ValidationMessage.RuleId), StringComparison.OrdinalIgnoreCase))
            {
                ruleId = reader.GetString();
            }
            else if (string.Equals(propertyName, nameof(ValidationMessage.Message), StringComparison.OrdinalIgnoreCase))
            {
                message = reader.GetString();
            }
            else if (string.Equals(propertyName, nameof(ValidationMessage.Metadata), StringComparison.OrdinalIgnoreCase))
            {
                metadata = reader.TokenType == JsonTokenType.Null
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(ref reader, options);
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException($"Unexpected end of JSON while reading {nameof(ValidationMessage)}.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        ValidationMessage value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (!string.IsNullOrWhiteSpace(value.Source))
        {
            writer.WriteString("source", value.Source);
        }

        if (!string.IsNullOrWhiteSpace(value.Identifier))
        {
            writer.WriteString("identifier", value.Identifier);
        }

        if (!string.IsNullOrWhiteSpace(value.Path))
        {
            writer.WriteString("path", value.Path);
        }

        if (!string.IsNullOrWhiteSpace(value.RuleId))
        {
            writer.WriteString("ruleId", value.RuleId);
        }

        writer.WriteString("message", value.Message);

        if (value.Metadata is { Count: > 0 })
        {
            writer.WritePropertyName("metadata");
            JsonSerializer.Serialize(writer, value.Metadata, options);
        }

        writer.WriteEndObject();
    }
}
