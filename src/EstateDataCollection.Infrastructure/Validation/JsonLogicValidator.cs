using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Infrastructure.Validation.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Validation;

public class JsonLogicValidator : IBusinessLogicValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly FrozenDictionary<string, IQualityRuleEngine> _engines;
    private readonly FrozenSet<string> _enabledEngines;
    private readonly ValidationOptions _options;
    private readonly JsonLogicValidatorInstrumentation _instrumentation;

    public JsonLogicValidator(
        IEnumerable<IQualityRuleEngine> engines,
        IOptions<ValidationOptions>? options = null,
        JsonLogicValidatorInstrumentation? instrumentation = null)
    {
        _options = options?.Value ?? new ValidationOptions();
        _instrumentation = instrumentation ?? new JsonLogicValidatorInstrumentation(NullLogger<JsonLogicValidator>.Instance);

        var engineList = engines?.ToList() ?? [];
        if (engineList.Count == 0)
        {
            engineList =
            [
                new JsonLogicQualityEngine(),
                new CelQualityEngine(options)
            ];
        }

        _engines = engineList.ToFrozenDictionary(
            e => e.EngineName,
            e => e,
            StringComparer.OrdinalIgnoreCase);

        _enabledEngines = (_options.EnabledEngines ?? ["jsonlogic", "cel"])
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public JsonLogicValidator(JsonLogicValidatorInstrumentation? instrumentation = null)
        : this([], null, instrumentation)
    {
    }

    public Result<IReadOnlyList<ValidationMessage>> ValidateRulesSyntax(
        string rawLogicJson,
        out IReadOnlyList<BusinessRule>? parsedRules)
    {
        parsedRules = null;

        if (string.IsNullOrWhiteSpace(rawLogicJson))
        {
            return Result<IReadOnlyList<ValidationMessage>>.Success([
                new ValidationMessage(typeof(JsonLogicValidator), null,  "Logic JSON was empty or null.")
            ]);
        }

        List<BusinessRule> rules;
        try
        {
            var deserialized = JsonSerializer.Deserialize<List<BusinessRule>>(rawLogicJson, JsonOptions);
            if (deserialized is null || deserialized.Count == 0)
            {
                return Result<IReadOnlyList<ValidationMessage>>.Success([
                    new ValidationMessage(typeof(JsonLogicValidator), null, "Logic JSON must contain at least one business rule.")
                ]);
            }
            rules = deserialized;
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ValidationMessage>>.Success([
                new ValidationMessage(typeof(JsonLogicValidator), null, $"Malformed Logic JSON syntax: {ex.Message}")
            ]);
        }

        var syntaxErrors = new List<ValidationMessage>();

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var ruleId = string.IsNullOrWhiteSpace(rule.Id) ? $"Rule[{i}]" : rule.Id;

            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, $"'id' is required."));
            }

            if (string.IsNullOrWhiteSpace(rule.Description))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, $"'description' is required."));
            }

            if (string.IsNullOrWhiteSpace(rule.Path))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, $"'path' target is required."));
            }

            // ODCS rules use 'description' + 'implementation', whereas standalone rules use 'message' + 'assert'
            if (string.IsNullOrWhiteSpace(rule.Message) && rule.Implementation is null)
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, $"'message' is required."));
            }

            var engineName = rule.ResolveEngine(_options.DefaultEngine);
            if (!TryResolveEngine(engineName, out var engine, out var engineError))
            {
                if (_options.UnknownEngineBehavior == UnknownEngineBehavior.Fail)
                {
                    syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, $"{engineError}"));
                }
                else
                {
                    _instrumentation.EngineSkipped(ruleId, engineName);
                }
                continue;
            }

            if (rule.EffectiveAssertion is null)
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, $"'assert' (or 'implementation') logic condition is required."));
            }
            else
            {
                var engineSyntaxErrors = engine!.ValidateSyntax(rule, ruleId);
                if (engineSyntaxErrors.Count > 0)
                {
                    syntaxErrors.AddRange(engineSyntaxErrors);
                }
            }
        }

        if (syntaxErrors.Count > 0)
        {
            return syntaxErrors;
        }

        parsedRules = rules;
        return Result<IReadOnlyList<ValidationMessage>>.Success([]);
    }

    public async Task<Result<BusinessRuleValidationResult>> ValidateAsync(
        string payloadJson,
        IReadOnlyList<BusinessRule> rules,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return Error.Validation(ErrorCodes.JsonEmptyPayload);
        }

        if (rules is null || rules.Count == 0)
        {
            return Result<BusinessRuleValidationResult>.Success(new BusinessRuleValidationResult());
        }

        try
        {
            _instrumentation.ValidationStarted(rules.Count);

            var rootNode = JsonNode.Parse(payloadJson);
            if (rootNode is null)
            {
                return Error.Validation(ErrorCodes.JsonEmptyPayload);
            }

            var violations = new List<ValidationError>();
            var resolvedRuleEngines = new List<(BusinessRule Rule, IQualityRuleEngine Engine)>(rules.Count);

            // Step 1: Resolve engines via O(1) FrozenDictionary lookup
            foreach (var rule in rules)
            {
                var isWarning = string.Equals(rule.Severity, "WARNING", StringComparison.OrdinalIgnoreCase);
                if (isWarning && !_options.TreatWarningsAsErrors)
                {
                    continue;
                }

                var engineName = rule.ResolveEngine(_options.DefaultEngine);
                if (!TryResolveEngine(engineName, out var engine, out var engineError))
                {
                    if (_options.UnknownEngineBehavior == UnknownEngineBehavior.Fail)
                    {
                        var path = string.IsNullOrWhiteSpace(rule.Path) ? "/" : $"/{rule.Path.Replace('.', '/')}";
                        violations.Add(new ValidationError(path, $"Rule '{rule.Id}': {engineError}"));
                    }
                    else
                    {
                        _instrumentation.EngineSkipped(rule.Id, engineName);
                    }
                    continue;
                }

                if (rule.EffectiveAssertion is not null)
                {
                    resolvedRuleEngines.Add((rule, engine!));
                }
            }

            if (resolvedRuleEngines.Count > 0)
            {
                var ambientContext = ExtractAmbientContext(rootNode);

                // Step 2: Group rules by target Path so each path in the JSON tree is traversed and enriched once
                foreach (var pathGroup in resolvedRuleEngines.GroupBy(x => x.Rule.Path ?? string.Empty, StringComparer.Ordinal))
                {
                    var targets = ResolvePathTargets(rootNode, pathGroup.Key);

                    foreach (var (instancePath, contextNode, childFocusNode) in targets)
                    {
                        var enrichedNode = EnrichContext(contextNode, childFocusNode, rootNode, ambientContext);

                        foreach (var (rule, engine) in pathGroup)
                        {
                            var evalOutcome = engine.Evaluate(rule, enrichedNode, rootNode);
                            if (evalOutcome.IsFailure)
                            {
                                var errPath = string.IsNullOrWhiteSpace(instancePath) ? rule.Path : instancePath;
                                violations.Add(new ValidationError(errPath, evalOutcome.Error.Description ?? $"Rule '{rule.Id}' evaluation failed.", RuleId: rule.Id));
                                continue;
                            }

                            if (!evalOutcome.Value)
                            {
                                var failurePath = string.IsNullOrWhiteSpace(instancePath) ? rule.Path : instancePath;
                                _instrumentation.RuleViolation(rule.Id, engine.EngineName, failurePath, rule.EffectiveMessage);
                                violations.Add(new ValidationError(failurePath, rule.EffectiveMessage, RuleId: rule.Id));
                            }
                        }
                    }
                }
            }

            if (violations.Count > 0)
            {
                _instrumentation.ValidationFailed(violations.Count);
                return new BusinessRuleValidationResult(violations);
            }

            _instrumentation.ValidationPassed();
            return new BusinessRuleValidationResult();
        }
        catch (Exception ex)
        {
            _instrumentation.ValidationMalformedJson(ex.Message);
            return Error.Validation(ErrorCodes.JsonMalformed, new { ex.Message });
        }
    }

    private bool TryResolveEngine(string engineName, out IQualityRuleEngine? engine, out string? errorMessage)
    {
        engine = null;
        errorMessage = null;

        if (!_enabledEngines.Contains(engineName))
        {
            var supported = string.Join(", ", _enabledEngines.OrderBy(x => x));
            errorMessage = $"Quality rule engine '{engineName}' is not enabled or supported. Supported engines: {supported}.";
            return false;
        }

        if (!_engines.TryGetValue(engineName, out engine))
        {
            var registered = string.Join(", ", _engines.Keys.OrderBy(x => x));
            errorMessage = $"Quality rule engine '{engineName}' is not registered. Registered engines: {registered}.";
            return false;
        }

        return true;
    }

    private static Dictionary<string, JsonNode?> ExtractAmbientContext(JsonNode root)
    {
        var context = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        var validSiteIds = new JsonArray();
        var validBlockIds = new JsonArray();
        var validEvidenceIds = new JsonArray();

        if (root["sites"] is JsonArray sites)
        {
            foreach (var site in sites)
            {
                if (site?["id"]?.GetValue<string>() is { } siteId)
                {
                    validSiteIds.Add(siteId);
                }
                if (site?["name"]?.GetValue<string>() is { } siteName &&
                    !validSiteIds.Any(x => x?.GetValue<string>() == siteName))
                {
                    validSiteIds.Add(siteName);
                }

                if (site?["blocks"] is JsonArray blocks)
                {
                    foreach (var block in blocks)
                    {
                        if (block?["id"]?.GetValue<string>() is { } blockId)
                        {
                            validBlockIds.Add(blockId);
                        }
                        if (block?["name"]?.GetValue<string>() is { } blockName &&
                            !validBlockIds.Any(x => x?.GetValue<string>() == blockName))
                        {
                            validBlockIds.Add(blockName);
                        }
                    }
                }
            }
        }

        if (root["evidence"] is JsonArray evidenceList)
        {
            foreach (var evidence in evidenceList)
            {
                if (evidence?["id"]?.GetValue<string>() is { } evId)
                {
                    validEvidenceIds.Add(evId);
                }
            }
        }

        context["valid_site_ids"] = validSiteIds;
        context["valid_block_ids"] = validBlockIds;
        context["valid_evidence_ids"] = validEvidenceIds;

        return context;
    }

    private static List<(string InstancePath, JsonNode ContextNode, JsonNode? ChildFocusNode)> ResolvePathTargets(
        JsonNode root,
        string pathPattern)
    {
        var results = new List<(string InstancePath, JsonNode ContextNode, JsonNode? ChildFocusNode)>();
        if (string.IsNullOrWhiteSpace(pathPattern) || pathPattern == "/")
        {
            results.Add(("/", root, root));
            return results;
        }

        var segments = pathPattern.Split('.', StringSplitOptions.RemoveEmptyEntries);
        TraverseSegments(root, segments, 0, "", results);
        return results;
    }

    private static void TraverseSegments(
        JsonNode currentNode,
        string[] segments,
        int segmentIndex,
        string currentJsonPointer,
        List<(string InstancePath, JsonNode ContextNode, JsonNode? ChildFocusNode)> accumulator)
    {
        if (segmentIndex >= segments.Length)
        {
            accumulator.Add((currentJsonPointer, currentNode, currentNode));
            return;
        }

        var segment = segments[segmentIndex];
        var isArrayTarget = segment.EndsWith("[]", StringComparison.Ordinal);
        var propertyName = isArrayTarget ? segment[..^2] : segment;

        // If this is the last segment and it is a property on the current context object (not an array []),
        // the context node for rule evaluation includes both the parent object and (if an object) the child property object,
        // while the reported failure path points to the target property.
        if (!isArrayTarget && segmentIndex == segments.Length - 1 && currentNode is JsonObject currentObj)
        {
            var targetPath = $"{currentJsonPointer}/{propertyName}";
            currentObj.TryGetPropertyValue(propertyName, out var childNode);
            accumulator.Add((targetPath, currentNode, childNode));
            return;
        }

        if (currentNode is not JsonObject obj || !obj.TryGetPropertyValue(propertyName, out var nextNode) || nextNode is null)
        {
            return;
        }

        var nextPointer = $"{currentJsonPointer}/{propertyName}";

        if (isArrayTarget)
        {
            if (nextNode is JsonArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is { } item)
                    {
                        TraverseSegments(item, segments, segmentIndex + 1, $"{nextPointer}/{i}", accumulator);
                    }
                }
            }
        }
        else
        {
            TraverseSegments(nextNode, segments, segmentIndex + 1, nextPointer, accumulator);
        }
    }

    private static JsonNode EnrichContext(
        JsonNode contextNode,
        JsonNode? childFocusNode,
        JsonNode rootNode,
        Dictionary<string, JsonNode?> ambientContext)
    {
        var enriched = JsonNode.Parse(contextNode.ToJsonString()) as JsonObject ?? new JsonObject();

        // If the rule targets a nested object property (e.g. "survey" or "replacement"),
        // merge that child object's properties into the context so both relative ("updated_at")
        // and parent-qualified ("survey.updated_at") references resolve cleanly.
        if (childFocusNode is JsonObject childObj && !ReferenceEquals(contextNode, childFocusNode))
        {
            foreach (var (key, value) in childObj)
            {
                if (!enriched.ContainsKey(key))
                {
                    enriched[key] = value is not null ? JsonNode.Parse(value.ToJsonString()) : null;
                }
            }
        }

        // Attach root top-level properties (survey, school, sites, elements, answers, evidence)
        // when not already present on the local target node.
        if (rootNode is JsonObject rootObj && !ReferenceEquals(contextNode, rootNode))
        {
            foreach (var (key, value) in rootObj)
            {
                if (!enriched.ContainsKey(key) && value is not null)
                {
                    enriched[key] = JsonNode.Parse(value.ToJsonString());
                }
            }
        }

        foreach (var (key, value) in ambientContext)
        {
            if (!enriched.ContainsKey(key) && value is not null)
            {
                enriched[key] = JsonNode.Parse(value.ToJsonString());
            }
        }

        // Add helper properties for answer validation
        if (enriched.TryGetPropertyValue("value", out var valNode))
        {
            var isNumeric = false;
            var isBoolean = false;

            if (valNode is JsonValue val)
            {
                if (val.TryGetValue<double>(out _) || (val.TryGetValue<string>(out var sNum) && double.TryParse(sNum, out _)))
                {
                    isNumeric = true;
                }
                if (val.TryGetValue<bool>(out _) || (val.TryGetValue<string>(out var sBool) && bool.TryParse(sBool, out _)))
                {
                    isBoolean = true;
                }
            }

            enriched["value_is_numeric"] = isNumeric;
            enriched["value_is_boolean"] = isBoolean;
        }

        if (!enriched.ContainsKey("question") && enriched.TryGetPropertyValue("asset_type", out var assetType))
        {
            enriched["question"] = new JsonObject
            {
                ["expected_asset_type"] = assetType?.GetValue<string>()
            };
        }

        return enriched;
    }
}
