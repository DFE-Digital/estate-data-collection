using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Celly;
using Celly.Interpreter;
using Celly.Providers;
using Celly.Values;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Validation.Engines;

public sealed partial class CelQualityEngine(
    IOptions<ValidationOptions>? options = null
) : IQualityRuleEngine
{
    public string EngineName => "cel";

    private readonly ValidationOptions _options = options?.Value ?? new ValidationOptions();

    private static readonly CelEnv SharedEnv = CelEnv.Create(new CelEnvSettings
    {
        EnableOptionalSyntax = true
    });

    private readonly ConcurrentDictionary<string, CelProgram> _compiledCache = new(StringComparer.Ordinal);

    [GeneratedRegex(@"(?<![A-Za-z0-9_])this\.", RegexOptions.CultureInvariant)]
    private static partial Regex ThisDotPrefixRegex();

    public IReadOnlyList<ValidationMessage> ValidateSyntax(BusinessRule rule, string ruleId)
    {
        var exprText = ExtractExpressionString(rule.EffectiveAssertion);
        if (string.IsNullOrWhiteSpace(exprText))
        {
            return [new ValidationMessage(typeof(CelQualityEngine), ruleId,$"CEL 'implementation' must be a non-empty expression string.")];
        }

        try
        {
            var normalized = NormalizeCelExpression(exprText);
            _ = _compiledCache.GetOrAdd(normalized, static expr => SharedEnv.Compile(expr));
            return [];
        }
        catch (Exception ex)
        {
            return [new ValidationMessage(typeof(CelQualityEngine), ruleId,$"Invalid CEL expression syntax: {ex.Message}")];
        }
    }

    public Result<bool> Evaluate(BusinessRule rule, JsonNode enrichedContextNode, JsonNode rootNode)
    {
        var exprText = ExtractExpressionString(rule.EffectiveAssertion);
        if (string.IsNullOrWhiteSpace(exprText))
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed, $"Rule '{rule.Id}' CEL expression is empty.");
        }

        try
        {
            var normalized = NormalizeCelExpression(exprText);
            var program = _compiledCache.GetOrAdd(normalized, static expr => SharedEnv.Compile(expr));

            var bindings = BuildBindings(enrichedContextNode, rootNode);
            var activation = new MapActivation(bindings, NativeTypeAdapter.Instance);
            var limits = new EvalLimits
            {
                MaxIterations = _options.CelMaxIterations > 0 ? _options.CelMaxIterations : 10_000
            };

            var result = program.Eval(activation, limits);

            if (result is ErrorValue errVal)
            {
                return Error.Validation(ErrorCodes.SchemaVerificationFailed, $"Rule '{rule.Id}' CEL evaluation error: {errVal}");
            }

            var native = result.ToNative();
            if (native is bool boolResult)
            {
                return boolResult;
            }

            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed,
                $"Rule '{rule.Id}' CEL expression must evaluate to a boolean, but returned '{result.Type.Name}'.");
        }
        catch (Exception ex)
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed, $"Rule '{rule.Id}' CEL evaluation failed: {ex.Message}");
        }
    }

    private static string? ExtractExpressionString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue val && val.TryGetValue<string>(out var str))
        {
            return str.Trim();
        }

        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("expression", out var exprNode) && exprNode is JsonValue ev && ev.TryGetValue<string>(out var es))
            {
                return es.Trim();
            }

            if (obj.TryGetPropertyValue("cel", out var celNode) && celNode is JsonValue cv && cv.TryGetValue<string>(out var cs))
            {
                return cs.Trim();
            }
        }

        return null;
    }

    private static string NormalizeCelExpression(string expression)
    {
        // Allow authors to write "this.field" in addition to "self.field" or direct "field"
        return ThisDotPrefixRegex().Replace(expression.Trim(), "self.");
    }

    private static Dictionary<string, object?> BuildBindings(JsonNode enrichedContextNode, JsonNode rootNode)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (enrichedContextNode is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                dict[key] = ConvertJsonNodeToClr(value);
            }
        }

        var selfValue = ConvertJsonNodeToClr(enrichedContextNode);
        dict["self"] = selfValue;
        dict["item"] = selfValue;
        dict["root"] = ConvertJsonNodeToClr(rootNode);

        return dict;
    }

    private static object? ConvertJsonNodeToClr(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue val)
        {
            var element = val.GetValue<JsonElement>();
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.Number when element.TryGetInt64(out var l) => l,
                JsonValueKind.Number when element.TryGetDouble(out var d) => d,
                JsonValueKind.String => element.GetString(),
                _ => val.ToString()
            };
        }

        if (node is JsonArray arr)
        {
            var list = new List<object?>(arr.Count);
            foreach (var item in arr)
            {
                list.Add(ConvertJsonNodeToClr(item));
            }
            return list;
        }

        if (node is JsonObject obj)
        {
            var map = new Dictionary<string, object?>(obj.Count, StringComparer.Ordinal);
            foreach (var (k, v) in obj)
            {
                map[k] = ConvertJsonNodeToClr(v);
            }
            return map;
        }

        return null;
    }
}
