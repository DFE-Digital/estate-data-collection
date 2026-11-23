using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using Json.Logic;

namespace EstateDataCollection.Infrastructure.Validation.Engines;

public sealed class JsonLogicQualityEngine : IQualityRuleEngine
{
    public string EngineName
    {
        get { return "jsonlogic"; }
    }

    private readonly ConcurrentDictionary<string, JsonNode> _normalizedAstCache = new(StringComparer.Ordinal);

    public IReadOnlyList<ValidationMessage> ValidateSyntax(BusinessRule rule, string ruleId)
    {
        var assertion = rule.EffectiveAssertion;
        if (assertion is null)
        {
            return [new ValidationMessage(typeof(JsonLogicQualityEngine), ruleId,$"'implementation' (or 'assert') logic condition is required.")];
        }

        if (assertion is not JsonObject)
        {
            return [new ValidationMessage(typeof(JsonLogicQualityEngine), ruleId,$"JsonLogic 'implementation' must be a JSON object expression.")];
        }

        try
        {
            var normalized = NormalizeRuleNode(assertion);
            var logicRule = JsonSerializer.Deserialize<Rule>(normalized.ToJsonString());
            if (logicRule is null)
            {
                return [new ValidationMessage(typeof(JsonLogicQualityEngine), ruleId,$"Failed to parse JsonLogic assertion rule.")];
            }

            return [];
        }
        catch (Exception ex)
        {
            return [new ValidationMessage(typeof(JsonLogicQualityEngine), ruleId, $"Invalid JsonLogic assertion syntax: {ex.Message}")];
        }
    }

    public Result<bool> Evaluate(BusinessRule rule, JsonNode enrichedContextNode, JsonNode rootNode)
    {
        var assertion = rule.EffectiveAssertion;
        if (assertion is null)
        {
            return true;
        }

        try
        {
            var rawKey = assertion.ToJsonString();
            var normalizedBase = _normalizedAstCache.GetOrAdd(rawKey, _ => NormalizeRuleNode(assertion));

            // Bind dynamic string/timestamp comparisons and .length properties against the current context
            var boundAst = BindContextAwareOperations(normalizedBase, enrichedContextNode);
            var logicRule = JsonSerializer.Deserialize<Rule>(boundAst.ToJsonString());
            
            if (logicRule is null)
            {
                return Error.Validation(ErrorCodes.SchemaVerificationFailed, $"Rule '{rule.Id}' could not be compiled.");
            }

            var evalResult = logicRule.Apply(enrichedContextNode);
            return IsTruthy(evalResult);
        }
        catch (Exception ex)
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed, $"Rule '{rule.Id}' evaluation failed: {ex.Message}");
        }
    }

    private static JsonNode NormalizeRuleNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var normalizedObj = new JsonObject();
            foreach (var (key, value) in obj)
            {
                if (key == "!in" && value is not null)
                {
                    normalizedObj["!"] = new JsonObject
                    {
                        ["in"] = NormalizeRuleNode(value)
                    };
                }
                else if (value is not null)
                {
                    normalizedObj[key] = NormalizeRuleNode(value);
                }
                else
                {
                    normalizedObj[key] = null;
                }
            }
            return normalizedObj;
        }

        if (node is JsonArray arr)
        {
            var normalizedArr = new JsonArray();
            foreach (var item in arr)
            {
                normalizedArr.Add(item is not null ? NormalizeRuleNode(item) : null);
            }
            return normalizedArr;
        }

        return JsonNode.Parse(node.ToJsonString())!;
    }

    /// <summary>
    /// Pre-processes a normalized JsonLogic AST against the evaluation context so that:
    /// 1. {"var": "foo.length"} on arrays/strings resolves to integer count/length.
    /// 2. Relational operators (&gt;, &gt;=, &lt;, &lt;=) comparing ISO-8601 timestamps or non-numeric strings
    ///    are converted into comparable numeric values (since Json.Logic 5.x only compares numeric values in &gt;, &gt;=, &lt;, &lt;=).
    /// </summary>
    private static JsonNode BindContextAwareOperations(JsonNode node, JsonNode contextNode)
    {
        if (node is JsonObject obj)
        {
            // Handle {"var": "..."} with .length suffix
            if (obj.Count == 1 && obj.TryGetPropertyValue("var", out var varArg) && varArg is JsonValue varVal &&
                varVal.TryGetValue<string>(out var varPath) && varPath.EndsWith(".length", StringComparison.Ordinal))
            {
                var targetPath = varPath[..^".length".Length];
                var resolvedTarget = ResolveVarPath(contextNode, targetPath);
                if (resolvedTarget is JsonArray targetArr)
                {
                    return JsonValue.Create(targetArr.Count);
                }
                if (resolvedTarget is JsonValue targetStrVal && targetStrVal.TryGetValue<string>(out var s))
                {
                    return JsonValue.Create(s.Length);
                }
                if (resolvedTarget is null)
                {
                    return JsonValue.Create(0);
                }
            }

            // Handle relational operators >, >=, <, <= when operands resolve to ISO-8601 timestamps or strings
            if (obj.Count == 1)
            {
                var kvp = obj.First();
                var op = kvp.Key;
                if (op is ">" or ">=" or "<" or "<=" && kvp.Value is JsonArray operands && operands.Count == 2)
                {
                    var leftBound = BindContextAwareOperations(operands[0]!, contextNode);
                    var rightBound = BindContextAwareOperations(operands[1]!, contextNode);

                    var leftStr = TryResolveStringOperand(leftBound, contextNode);
                    var rightStr = TryResolveStringOperand(rightBound, contextNode);

                    if (leftStr is not null && rightStr is not null)
                    {
                        // 1. Try ISO-8601 DateTimeOffset comparison
                        if (DateTimeOffset.TryParse(leftStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var leftDt) &&
                            DateTimeOffset.TryParse(rightStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var rightDt))
                        {
                            return new JsonObject
                            {
                                [op] = new JsonArray(JsonValue.Create(leftDt.UtcTicks), JsonValue.Create(rightDt.UtcTicks))
                            };
                        }

                        // 2. If neither is a plain number, compare strings ordinally
                        if (!double.TryParse(leftStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                            !double.TryParse(rightStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        {
                            var cmp = string.CompareOrdinal(leftStr, rightStr);
                            return new JsonObject
                            {
                                [op] = new JsonArray(JsonValue.Create(cmp), JsonValue.Create(0))
                            };
                        }
                    }

                    return new JsonObject
                    {
                        [op] = new JsonArray(leftBound, rightBound)
                    };
                }
            }

            var boundObj = new JsonObject();
            foreach (var (key, value) in obj)
            {
                boundObj[key] = value is not null ? BindContextAwareOperations(value, contextNode) : null;
            }
            return boundObj;
        }

        if (node is JsonArray arr)
        {
            var boundArr = new JsonArray();
            foreach (var item in arr)
            {
                boundArr.Add(item is not null ? BindContextAwareOperations(item, contextNode) : null);
            }
            return boundArr;
        }

        return JsonNode.Parse(node.ToJsonString())!;
    }

    private static string? TryResolveStringOperand(JsonNode operandNode, JsonNode contextNode)
    {
        if (operandNode is JsonValue literalVal && literalVal.TryGetValue<string>(out var literalStr))
        {
            return literalStr;
        }

        if (operandNode is JsonObject varObj && varObj.Count == 1 && varObj.TryGetPropertyValue("var", out var varArg))
        {
            string? path = null;
            if (varArg is JsonValue v && v.TryGetValue<string>(out var s))
            {
                path = s;
            }
            else if (varArg is JsonArray varArr && varArr.Count > 0 && varArr[0] is JsonValue v0 && v0.TryGetValue<string>(out var s0))
            {
                path = s0;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                var resolved = ResolveVarPath(contextNode, path);
                if (resolved is JsonValue rv && rv.TryGetValue<string>(out var resolvedStr))
                {
                    return resolvedStr;
                }
            }
        }

        return null;
    }

    private static JsonNode? ResolveVarPath(JsonNode? root, string dotPath)
    {
        if (root is null || string.IsNullOrWhiteSpace(dotPath))
        {
            return root;
        }

        var current = root;
        foreach (var segment in dotPath.Split('.'))
        {
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out current))
                {
                    return null;
                }
            }
            else if (current is JsonArray arr && int.TryParse(segment, out var index) && index >= 0 && index < arr.Count)
            {
                current = arr[index];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static bool IsTruthy(JsonNode? node)
    {
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue val)
        {
            if (val.TryGetValue<bool>(out var b))
            {
                return b;
            }

            if (val.TryGetValue<double>(out var num))
            {
                return num != 0;
            }

            if (val.TryGetValue<int>(out var i))
            {
                return i != 0;
            }

            if (val.TryGetValue<long>(out var l))
            {
                return l != 0;
            }

            if (val.TryGetValue<string>(out var s))
            {
                return !string.IsNullOrEmpty(s);
            }
        }

        if (node is JsonArray arr)
        {
            return arr.Count > 0;
        }

        return true;
    }
}
