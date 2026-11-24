using System.Text.Json;
using System.Text.Json.Nodes;

namespace EstateDataCollection.Domain.Schema;

/// <summary>
/// Parses an invalid test fixture entry which can be either:
/// 1) A raw JSON payload object ({ ... }), or
/// 2) An annotated expectation wrapper: { "description"?: "...", "payload": { ... }, "expectedErrors"?: ["RULE-..."] }.
/// </summary>
public static class InvalidFixtureParser
{
    private static readonly HashSet<string> WrapperAllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "payload",
        "expectedErrors",
        "expectedError",
        "description",
        "name"
    };

    public static InvalidFixtureSpec Parse(string rawEntry)
    {
        if (string.IsNullOrWhiteSpace(rawEntry))
        {
            return new InvalidFixtureSpec(rawEntry, []);
        }

        try
        {
            var node = JsonNode.Parse(rawEntry);
            if (node is not JsonObject obj)
            {
                return new InvalidFixtureSpec(rawEntry, []);
            }

            string? payloadKey = null;
            string? expectedErrorsKey = null;
            string? expectedErrorKey = null;
            string? descriptionKey = null;

            foreach (var kvp in obj)
            {
                if (!WrapperAllowedKeys.Contains(kvp.Key))
                {
                    return new InvalidFixtureSpec(rawEntry, []);
                }

                if (string.Equals(kvp.Key, "payload", StringComparison.OrdinalIgnoreCase))
                {
                    payloadKey = kvp.Key;
                }
                else if (string.Equals(kvp.Key, "expectedErrors", StringComparison.OrdinalIgnoreCase))
                {
                    expectedErrorsKey = kvp.Key;
                }
                else if (string.Equals(kvp.Key, "expectedError", StringComparison.OrdinalIgnoreCase))
                {
                    expectedErrorKey = kvp.Key;
                }
                else if (string.Equals(kvp.Key, "description", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(kvp.Key, "name", StringComparison.OrdinalIgnoreCase))
                {
                    descriptionKey ??= kvp.Key;
                }
            }

            if (payloadKey is null)
            {
                return new InvalidFixtureSpec(rawEntry, []);
            }

            var payloadNode = obj[payloadKey];
            string payloadJson;
            if (payloadNode is null)
            {
                payloadJson = "null";
            }
            else if (payloadNode is JsonValue val && val.TryGetValue<string>(out var rawStr))
            {
                payloadJson = rawStr;
            }
            else
            {
                payloadJson = payloadNode.ToJsonString();
            }

            var expected = new List<string>();
            if (expectedErrorsKey is not null && obj[expectedErrorsKey] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JsonValue itemVal && itemVal.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                    {
                        expected.Add(text.Trim());
                    }
                }
            }
            else if (expectedErrorsKey is not null && obj[expectedErrorsKey] is JsonValue singleArrVal &&
                     singleArrVal.TryGetValue<string>(out var singleArrStr) && !string.IsNullOrWhiteSpace(singleArrStr))
            {
                expected.Add(singleArrStr.Trim());
            }

            if (expectedErrorKey is not null && obj[expectedErrorKey] is JsonValue singleVal &&
                singleVal.TryGetValue<string>(out var singleStr) && !string.IsNullOrWhiteSpace(singleStr))
            {
                expected.Add(singleStr.Trim());
            }

            string? description = null;
            if (descriptionKey is not null && obj[descriptionKey] is JsonValue descVal &&
                descVal.TryGetValue<string>(out var descStr) && !string.IsNullOrWhiteSpace(descStr))
            {
                description = descStr.Trim();
            }

            return new InvalidFixtureSpec(
                PayloadJson: payloadJson,
                ExpectedErrors: expected.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Description: description,
                HasWrapper: true);
        }
        catch (JsonException)
        {
            return new InvalidFixtureSpec(rawEntry, []);
        }
    }
}
