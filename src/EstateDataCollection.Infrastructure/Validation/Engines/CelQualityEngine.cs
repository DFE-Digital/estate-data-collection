using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Celly;
using Celly.Common;
using Celly.Interpreter;
using Celly.Providers;
using Celly.Stdlib;
using Celly.Values;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Validation.Engines;

public sealed class CelQualityEngine : IQualityRuleEngine
{
    private const string ParentAlias = "__edc_parent";
    private const string AncestorsAlias = "__edc_ancestors";
    private const string RootAlias = "__edc_root";
    private const string IndexAlias = "__edc_index";

    private readonly ValidationOptions _options;
    private readonly CelEnv _env;
    private readonly ConcurrentDictionary<string, CelProgram> _compiledCache = new(StringComparer.Ordinal);

    public CelQualityEngine(IOptions<ValidationOptions>? options = null)
    {
        _options = options?.Value ?? new ValidationOptions();
        _env = CelEnv.Create(new CelEnvSettings
        {
            EnableOptionalSyntax = true,
            ConfigureFunctions = ConfigureToleranceAndArithmeticOperators
        });

        Descriptor = new ValidationEngineDescriptor(
            EngineId: "cel",
            DisplayName: "CEL Quality Engine",
            Kind: ValidationEngineKinds.QualityRule,
            Description: $"Evaluates Common Expression Language (CEL) quality rules with RFC3339 timestamps, comprehensions, ancestor context, automatic float epsilon tolerance, compiled program caching, and {(_options.CelMaxIterations > 0 ? _options.CelMaxIterations : 10_000)} max iterations.");
    }

    public string EngineName
    {
        get { return "cel"; }
    }

    public ValidationEngineDescriptor Descriptor { get; }

    public IReadOnlyList<ValidationMessage> ValidateSyntax(BusinessRule rule, string ruleId)
    {
        var exprText = ExtractExpressionString(rule.EffectiveAssertion);
        if (string.IsNullOrWhiteSpace(exprText))
        {
            return [new ValidationMessage(typeof(CelQualityEngine), ruleId, "CEL 'implementation' must be a non-empty expression string.", source: ValidationSources.Rule, ruleId: ruleId)];
        }

        try
        {
            var normalized = NormaliseCelExpression(exprText);
            _ = _compiledCache.GetOrAdd(normalized, expr => _env.Compile(expr));
            return [];
        }
        catch (Exception ex)
        {
            return [new ValidationMessage(typeof(CelQualityEngine), ruleId, $"Invalid CEL expression syntax: {ex.Message}", source: ValidationSources.Rule, ruleId: ruleId)];
        }
    }

    public Result<bool> Evaluate(BusinessRule rule, JsonNode enrichedContextNode, JsonNode rootNode)
    {
        var exprText = ExtractExpressionString(rule.EffectiveAssertion);
        if (string.IsNullOrWhiteSpace(exprText))
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed.GetName(), $"Rule '{rule.Id}' CEL expression is empty.");
        }

        try
        {
            var normalized = NormaliseCelExpression(exprText);
            var program = _compiledCache.GetOrAdd(normalized, expr => _env.Compile(expr));

            var bindings = BuildBindings(enrichedContextNode, rootNode);
            var activation = new MapActivation(bindings, NativeTypeAdapter.Instance);
            var limits = new EvalLimits
            {
                MaxIterations = _options.CelMaxIterations > 0 ? _options.CelMaxIterations : 10_000
            };

            var effectiveEpsilon = NumericToleranceScope.Current
                                   ?? (_options.CurrencyRoundingTolerance is { } currency
                                       ? (double)currency
                                       : _options.FloatArtifactEpsilon);

            using var _ = NumericToleranceScope.Push(effectiveEpsilon);
            var result = program.Eval(activation, limits);

            if (result is ErrorValue errVal)
            {
                return Error.Validation(ErrorCodes.SchemaVerificationFailed.GetName(), $"Rule '{rule.Id}' CEL evaluation error: {errVal}");
            }

            var native = result.ToNative();
            if (native is bool boolResult)
            {
                return boolResult;
            }

            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed.GetName(),
                $"Rule '{rule.Id}' CEL expression must evaluate to a boolean, but returned '{result.Type.Name}'.");
        }
        catch (Exception ex)
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed.GetName(), $"Rule '{rule.Id}' CEL evaluation failed: {ex.Message}");
        }
    }

    private void ConfigureToleranceAndArithmeticOperators(FunctionRegistry registry)
    {
        // Promote mixed int/double JSON numeric operands cleanly for ODCS-generated expressions (e.g. qty * rate)
        WrapMixedDoubleArithmetic(registry, Operators.Add);
        WrapMixedDoubleArithmetic(registry, Operators.Subtract);
        WrapMixedDoubleArithmetic(registry, Operators.Multiply);
        WrapMixedDoubleArithmetic(registry, Operators.Divide);

        WrapComparisonOperator(registry, Operators.Equals, CelComparisonOp.Equals);
        WrapComparisonOperator(registry, Operators.NotEquals, CelComparisonOp.NotEquals);
        WrapComparisonOperator(registry, Operators.Less, CelComparisonOp.Less);
        WrapComparisonOperator(registry, Operators.LessEquals, CelComparisonOp.LessEquals);
        WrapComparisonOperator(registry, Operators.Greater, CelComparisonOp.Greater);
        WrapComparisonOperator(registry, Operators.GreaterEquals, CelComparisonOp.GreaterEquals);
    }

    private static void WrapMixedDoubleArithmetic(FunctionRegistry registry, string operatorName)
    {
        var original = registry.Find(operatorName);
        if (original is null)
        {
            return;
        }

        registry.Register(operatorName, args =>
        {
            if (args.Length == 2 &&
                (args[0] is DoubleValue || args[1] is DoubleValue) &&
                !(args[0] is DoubleValue && args[1] is DoubleValue) &&
                TryGetDouble(args[0], out var left) &&
                TryGetDouble(args[1], out var right))
            {
                return original([DoubleValue.Of(left), DoubleValue.Of(right)]);
            }

            return original(args);
        });
    }

    private enum CelComparisonOp
    {
        Equals,
        NotEquals,
        Less,
        LessEquals,
        Greater,
        GreaterEquals
    }

    private void WrapComparisonOperator(FunctionRegistry registry, string operatorName, CelComparisonOp op)
    {
        var original = registry.Find(operatorName);
        if (original is null)
        {
            return;
        }

        registry.Register(operatorName, args =>
        {
            if (args.Length != 2)
            {
                return original(args);
            }

            var eps = NumericToleranceScope.Current
                      ?? (_options.CurrencyRoundingTolerance is { } currency
                          ? (double)currency
                          : _options.FloatArtifactEpsilon);

            if (eps <= 0)
            {
                return original(args);
            }

            // Apply automatic epsilon tolerance whenever at least one operand is a floating-point DoubleValue
            // and both operands are numeric (so integer == integer stays exact).
            if ((args[0] is DoubleValue || args[1] is DoubleValue) &&
                TryGetDouble(args[0], out var left) &&
                TryGetDouble(args[1], out var right) &&
                !double.IsNaN(left) && !double.IsNaN(right) &&
                !double.IsInfinity(left) && !double.IsInfinity(right))
            {
                var diff = left - right;
                var result = op switch
                {
                    CelComparisonOp.Equals => Math.Abs(diff) <= eps,
                    CelComparisonOp.NotEquals => Math.Abs(diff) > eps,
                    CelComparisonOp.Greater => diff > eps,
                    CelComparisonOp.GreaterEquals => diff >= -eps,
                    CelComparisonOp.Less => diff < -eps,
                    CelComparisonOp.LessEquals => diff <= eps,
                    _ => false
                };

                return BoolValue.Of(result);
            }

            return original(args);
        });
    }

    private static bool TryGetDouble(CelValue val, out double result)
    {
        switch (val)
        {
            case DoubleValue d:
                result = d.Value;
                return true;
            case IntValue i:
                result = i.Value;
                return true;
            case UintValue u:
                result = u.Value;
                return true;
            default:
                result = 0;
                return false;
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

    private static string NormaliseCelExpression(string expression)
    {
        // Rewrite outside single/double-quoted string literals:
        // - "this." -> "self."
        // - "$parent", "$ancestors", "$root", "$index" -> CEL-valid identifiers ("__edc_parent", etc.)
        var input = expression.Trim();
        var sb = new StringBuilder(input.Length + 16);
        var i = 0;
        char? quoteChar = null;

        while (i < input.Length)
        {
            var ch = input[i];

            if (quoteChar.HasValue)
            {
                sb.Append(ch);
                if (ch == '\\' && i + 1 < input.Length)
                {
                    sb.Append(input[i + 1]);
                    i += 2;
                    continue;
                }

                if (ch == quoteChar.Value)
                {
                    quoteChar = null;
                }

                i++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quoteChar = ch;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == '$')
            {
                var prevOk = i == 0 || !IsIdentChar(input[i - 1]);
                if (prevOk)
                {
                    if (MatchesToken(input, i, "$parent"))
                    {
                        sb.Append(ParentAlias);
                        i += "$parent".Length;
                        continue;
                    }

                    if (MatchesToken(input, i, "$ancestors"))
                    {
                        sb.Append(AncestorsAlias);
                        i += "$ancestors".Length;
                        continue;
                    }

                    if (MatchesToken(input, i, "$root"))
                    {
                        sb.Append(RootAlias);
                        i += "$root".Length;
                        continue;
                    }

                    if (MatchesToken(input, i, "$index"))
                    {
                        sb.Append(IndexAlias);
                        i += "$index".Length;
                        continue;
                    }
                }
            }

            if (ch == 't' && (i == 0 || !IsIdentChar(input[i - 1])) &&
                input.AsSpan(i).StartsWith("this.", StringComparison.Ordinal))
            {
                sb.Append("self.");
                i += "this.".Length;
                continue;
            }

            sb.Append(ch);
            i++;
        }

        return sb.ToString();
    }

    private static bool MatchesToken(string input, int index, string token)
    {
        if (!input.AsSpan(index).StartsWith(token, StringComparison.Ordinal))
        {
            return false;
        }

        var end = index + token.Length;
        return end >= input.Length || !IsIdentChar(input[end]);
    }

    private static bool IsIdentChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static Dictionary<string, object?> BuildBindings(JsonNode enrichedContextNode, JsonNode rootNode)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (enrichedContextNode is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                dict[MapBindingKey(key)] = ConvertJsonNodeToClr(value);
            }
        }

        var selfValue = ConvertJsonNodeToClr(enrichedContextNode);
        dict["self"] = selfValue;
        dict["item"] = selfValue;
        dict["root"] = ConvertJsonNodeToClr(rootNode);

        if (!dict.ContainsKey(RootAlias))
        {
            dict[RootAlias] = dict["root"];
        }

        return dict;
    }

    private static string MapBindingKey(string key)
    {
        return key switch
        {
            "$parent" => ParentAlias,
            "$ancestors" => AncestorsAlias,
            "$root" => RootAlias,
            "$index" => IndexAlias,
            _ => key
        };
    }

    private static object? ConvertJsonNodeToClr(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue val)
        {
            if (val.TryGetValue<JsonElement>(out var element))
            {
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

            if (val.TryGetValue<bool>(out var b))
            {
                return b;
            }

            if (val.TryGetValue<long>(out var lVal))
            {
                return lVal;
            }

            if (val.TryGetValue<int>(out var iVal))
            {
                return (long)iVal;
            }

            if (val.TryGetValue<double>(out var dVal))
            {
                return dVal;
            }

            if (val.TryGetValue<decimal>(out var decVal))
            {
                return (double)decVal;
            }

            if (val.TryGetValue<string>(out var sVal))
            {
                return sVal;
            }

            return val.ToString();
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
                map[MapBindingKey(k)] = ConvertJsonNodeToClr(v);
            }
            return map;
        }

        return null;
    }
}
