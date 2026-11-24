using System.Text.Json;
using System.Text.Json.Serialization;

namespace EstateDataCollection.Common.Results;

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