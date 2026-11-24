using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;

namespace EstateDataCollection.Infrastructure.Validation.Enrichment;

/// <summary>
/// Enriches individual rule target nodes with derived survey answer helper properties
/// (<c>value_is_numeric</c>, <c>value_is_boolean</c>, and <c>question.expected_asset_type</c>).
/// </summary>
public sealed class SurveyAnswerContextEnricher : IRuleContextEnricher
{
    public void EnrichTargetContext(JsonObject enrichedTarget, JsonNode? childFocusNode, JsonNode rootNode)
    {
        if (enrichedTarget.TryGetPropertyValue("value", out var valNode))
        {
            var isNumeric = false;
            var isBoolean = false;

            if (valNode is JsonValue val)
            {
                if (val.TryGetValue<double>(out _) ||
                    (val.TryGetValue<string>(out var sNum) && double.TryParse(sNum, out _)))
                {
                    isNumeric = true;
                }

                if (val.TryGetValue<bool>(out _) ||
                    (val.TryGetValue<string>(out var sBool) && bool.TryParse(sBool, out _)))
                {
                    isBoolean = true;
                }
            }

            enrichedTarget["value_is_numeric"] = isNumeric;
            enrichedTarget["value_is_boolean"] = isBoolean;
        }

        if (!enrichedTarget.ContainsKey("question") &&
            enrichedTarget.TryGetPropertyValue("asset_type", out var assetType))
        {
            enrichedTarget["question"] = new JsonObject
            {
                ["expected_asset_type"] = assetType?.GetValue<string>()
            };
        }
    }
}
