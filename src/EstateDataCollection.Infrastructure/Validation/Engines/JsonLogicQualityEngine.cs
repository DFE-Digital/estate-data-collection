using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using Json.Logic;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Validation.Engines;

public sealed class JsonLogicQualityEngine(
    IOptions<ValidationOptions>? options = null
) : IQualityRuleEngine
{
    private const int MaxAstDepth = 64;

    private static readonly object RegistryLock = new();
    private static bool _toleranceHandlersInstalled;

    public static readonly WorkflowEngineDescriptor EngineDescriptor = new(
        EngineId: "jsonlogic",
        DisplayName: "JsonLogic Quality Engine",
        Kind: ValidationEngineKinds.QualityRule,
        Description: "Evaluates structured JSON Logic quality rules with ISO-8601 comparison, .length property binding, !in operator, ancestor context, and automatic float epsilon tolerance.");

    private readonly ValidationOptions _options = options?.Value ?? new ValidationOptions();
    private readonly ConcurrentDictionary<string, JsonNode> _normalizedAstCache = new(StringComparer.Ordinal);

    static JsonLogicQualityEngine()
    {
        EnsureToleranceComparisonHandlers();
    }

    public string EngineName => EngineDescriptor.EngineId;

    public WorkflowEngineDescriptor Descriptor => EngineDescriptor;

    public IReadOnlyList<ValidationMessage> ValidateSyntax(BusinessRule rule, string ruleId)
    {
        var assertion = rule.EffectiveAssertion;
        if (assertion is null)
        {
            return [new ValidationMessage(typeof(JsonLogicQualityEngine), ruleId, "'implementation' (or 'assert') logic condition is required.", source: ValidationSources.Rule, ruleId: ruleId)];
        }

        if (assertion is not JsonObject)
        {
            return [new ValidationMessage(typeof(JsonLogicQualityEngine), ruleId, "JsonLogic 'implementation' must be a JSON object expression.", source: ValidationSources.Rule, ruleId: ruleId)];
        }

        try
        {
            var normalized = NormaliseRuleNode(assertion);
            var logicRule = JsonSerializer.Deserialize<Rule>(normalized.ToJsonString());
            if (logicRule is null)
            {
                return [new ValidationMessage(typeof(JsonLogicQualityEngine), ruleId, "Failed to parse JsonLogic assertion rule.", source: ValidationSources.Rule, ruleId: ruleId)];
            }

            return [];
        }
        catch (Exception ex)
        {
            return [new ValidationMessage(typeof(JsonLogicQualityEngine), ruleId, $"Invalid JsonLogic assertion syntax: {ex.Message}", source: ValidationSources.Rule, ruleId: ruleId)];
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
            var normalizedBase = _normalizedAstCache.GetOrAdd(rawKey, _ => NormaliseRuleNode(assertion));

            // Bind dynamic string/timestamp comparisons and .length properties against the current context
            var boundAst = BindContextAwareOperations(normalizedBase, enrichedContextNode);

            // Validate deserialization structure if not cached, then evaluate through RuleRegistry handlers
            var effectiveEpsilon = NumericToleranceScope.Current
                                   ?? (_options.CurrencyRoundingTolerance is { } currency
                                       ? (double)currency
                                       : _options.FloatArtifactEpsilon);

            using var _ = NumericToleranceScope.Push(effectiveEpsilon);
            var evalResult = Json.Logic.JsonLogic.Apply(boundAst, enrichedContextNode);
            return IsTruthy(evalResult);
        }
        catch (Exception ex)
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed, $"Rule '{rule.Id}' evaluation failed: {ex.Message}");
        }
    }

    private static void EnsureToleranceComparisonHandlers()
    {
        lock (RegistryLock)
        {
            if (_toleranceHandlersInstalled)
            {
                return;
            }

            WrapOperator("==", ComparisonOperator.Equals);
            WrapOperator("===", ComparisonOperator.Equals);
            WrapOperator("!=", ComparisonOperator.NotEquals);
            WrapOperator("!==", ComparisonOperator.NotEquals);
            WrapOperator("<", ComparisonOperator.LessThan);
            WrapOperator("<=", ComparisonOperator.LessThanOrEqual);
            WrapOperator(">", ComparisonOperator.MoreThan);
            WrapOperator(">=", ComparisonOperator.MoreThanOrEqual);

            _toleranceHandlersInstalled = true;
        }
    }

    private static void WrapOperator(string opSymbol, ComparisonOperator opKind)
    {
        var existing = RuleRegistry.GetHandler(opSymbol);
        if (existing is null or ToleranceComparisonRule)
        {
            return;
        }

        RuleRegistry.AddRule(opSymbol, new ToleranceComparisonRule(opKind, existing));
    }

    private enum ComparisonOperator
    {
        Equals,
        NotEquals,
        LessThan,
        LessThanOrEqual,
        MoreThan,
        MoreThanOrEqual
    }

    private sealed class ToleranceComparisonRule(ComparisonOperator op, IRule inner) : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            var eps = NumericToleranceScope.Current;
            if (eps is not > 0 || args is not JsonArray { Count: 2 } arr)
            {
                return inner.Apply(args, context);
            }

            var leftVal = Json.Logic.JsonLogic.Apply(arr[0], context);
            var rightVal = Json.Logic.JsonLogic.Apply(arr[1], context);

            if (!TryGetJsonNumber(leftVal, out var leftDec, out var leftDbl) ||
                !TryGetJsonNumber(rightVal, out var rightDec, out var rightDbl))
            {
                return inner.Apply(args, context);
            }

            bool result;
            if (leftDec.HasValue && rightDec.HasValue &&
                eps.Value <= (double)decimal.MaxValue && eps.Value >= 1e-28)
            {
                var diff = leftDec.Value - rightDec.Value;
                var epsDec = (decimal)eps.Value;
                result = op switch
                {
                    ComparisonOperator.Equals => Math.Abs(diff) <= epsDec,
                    ComparisonOperator.NotEquals => Math.Abs(diff) > epsDec,
                    ComparisonOperator.MoreThan => diff > epsDec,
                    ComparisonOperator.MoreThanOrEqual => diff >= -epsDec,
                    ComparisonOperator.LessThan => diff < -epsDec,
                    ComparisonOperator.LessThanOrEqual => diff <= epsDec,
                    _ => false
                };
            }
            else
            {
                var diff = leftDbl - rightDbl;
                if (double.IsNaN(diff) || double.IsInfinity(diff))
                {
                    return inner.Apply(args, context);
                }

                result = op switch
                {
                    ComparisonOperator.Equals => Math.Abs(diff) <= eps.Value,
                    ComparisonOperator.NotEquals => Math.Abs(diff) > eps.Value,
                    ComparisonOperator.MoreThan => diff > eps.Value,
                    ComparisonOperator.MoreThanOrEqual => diff >= -eps.Value,
                    ComparisonOperator.LessThan => diff < -eps.Value,
                    ComparisonOperator.LessThanOrEqual => diff <= eps.Value,
                    _ => false
                };
            }

            return JsonValue.Create(result);
        }

        private static bool TryGetJsonNumber(JsonNode? node, out decimal? decValue, out double dblValue)
        {
            decValue = null;
            dblValue = 0;

            if (node is not JsonValue jv)
            {
                return false;
            }

            if (jv.TryGetValue<bool>(out _))
            {
                return false;
            }

            if (jv.TryGetValue<JsonElement>(out var element))
            {
                if (element.ValueKind != JsonValueKind.Number)
                {
                    return false;
                }

                if (element.TryGetDecimal(out var elemDec))
                {
                    decValue = elemDec;
                }

                return element.TryGetDouble(out dblValue);
            }

            if (jv.TryGetValue<decimal>(out var d))
            {
                decValue = d;
                dblValue = (double)d;
                return true;
            }

            if (jv.TryGetValue<long>(out var l))
            {
                decValue = l;
                dblValue = l;
                return true;
            }

            if (jv.TryGetValue<int>(out var i))
            {
                decValue = i;
                dblValue = i;
                return true;
            }

            if (jv.TryGetValue<double>(out var dbl))
            {
                dblValue = dbl;
                try
                {
                    if (dbl >= (double)decimal.MinValue && dbl <= (double)decimal.MaxValue)
                    {
                        decValue = Convert.ToDecimal(dbl, CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    // Fallback to double comparison
                }
                return true;
            }

            return false;
        }
    }

    private static JsonNode NormaliseRuleNode(JsonNode node, int depth = 0)
    {
        if (depth > MaxAstDepth)
        {
            throw new InvalidOperationException($"JsonLogic rule exceeds maximum nesting depth of {MaxAstDepth}.");
        }

        if (node is JsonObject obj)
        {
            var normalizedObj = new JsonObject();
            foreach (var (key, value) in obj)
            {
                if (key == "!in" && value is not null)
                {
                    normalizedObj["!"] = new JsonObject
                    {
                        ["in"] = NormaliseRuleNode(value, depth + 1)
                    };
                }
                else if (value is not null)
                {
                    normalizedObj[key] = NormaliseRuleNode(value, depth + 1);
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
                normalizedArr.Add(item is not null ? NormaliseRuleNode(item, depth + 1) : null);
            }
            return normalizedArr;
        }

        return node.DeepClone();
    }

    /// <summary>
    /// Pre-processes a normalized JsonLogic AST against the evaluation context so that:
    /// 1. {"var": "foo.length"} on arrays/strings resolves to integer count/length.
    /// 2. Relational operators (&gt;, &gt;=, &lt;, &lt;=) comparing ISO-8601 timestamps or non-numeric strings
    ///    are converted into comparable numeric values (since Json.Logic only compares numeric values in &gt;, &gt;=, &lt;, &lt;=).
    /// </summary>
    private static JsonNode BindContextAwareOperations(JsonNode node, JsonNode contextNode, int depth = 0)
    {
        if (depth > MaxAstDepth)
        {
            throw new InvalidOperationException($"JsonLogic rule exceeds maximum nesting depth of {MaxAstDepth}.");
        }

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
                    var leftBound = BindContextAwareOperations(operands[0]!, contextNode, depth + 1);
                    var rightBound = BindContextAwareOperations(operands[1]!, contextNode, depth + 1);

                    var leftStr = TryResolveStringOperand(leftBound, contextNode);
                    var rightStr = TryResolveStringOperand(rightBound, contextNode);

                    if (leftStr is not null && rightStr is not null)
                    {
                        // 1. Try exact ISO-8601 DateTimeOffset comparison without losing sub-second precision
                        if (DateTimeOffset.TryParse(leftStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var leftDt) &&
                            DateTimeOffset.TryParse(rightStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var rightDt))
                        {
                            var dtCmp = leftDt.CompareTo(rightDt);
                            return new JsonObject
                            {
                                [op] = new JsonArray(JsonValue.Create(dtCmp), JsonValue.Create(0))
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
                boundObj[key] = value is not null ? BindContextAwareOperations(value, contextNode, depth + 1) : null;
            }
            return boundObj;
        }

        if (node is JsonArray arr)
        {
            var boundArr = new JsonArray();
            foreach (var item in arr)
            {
                boundArr.Add(item is not null ? BindContextAwareOperations(item, contextNode, depth + 1) : null);
            }
            return boundArr;
        }

        return node.DeepClone();
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
            else if (current is JsonArray arr && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < arr.Count)
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
