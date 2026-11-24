using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation.Engines;
using EstateDataCollection.Infrastructure.Validation.Enrichment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Validation;

public class JsonLogicValidator : IBusinessLogicValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly FrozenDictionary<string, IQualityRuleEngine> _engines;
    private readonly IReadOnlyList<ValidationEngineDescriptor> _registeredDescriptors;
    private readonly FrozenSet<string> _enabledEngines;
    private readonly IReadOnlyList<IRuleContextEnricher> _contextEnrichers;
    private readonly ValidationOptions _options;
    private readonly JsonLogicValidatorInstrumentation _instrumentation;

    [ActivatorUtilitiesConstructor]
    public JsonLogicValidator(
        IEnumerable<IQualityRuleEngine> engines,
        IEnumerable<IRuleContextEnricher>? contextEnrichers,
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

        _registeredDescriptors = engineList
            .Select(e => e.Descriptor)
            .ToArray();

        _enabledEngines = (_options.EnabledEngines ?? ["jsonlogic", "cel"])
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        _contextEnrichers = contextEnrichers is null
            ? [new EstateAmbientReferenceEnricher(), new SurveyAnswerContextEnricher()]
            : contextEnrichers.ToArray();
    }

    public JsonLogicValidator(
        IEnumerable<IQualityRuleEngine> engines,
        IOptions<ValidationOptions>? options = null,
        JsonLogicValidatorInstrumentation? instrumentation = null)
        : this(engines, contextEnrichers: null, options, instrumentation)
    {
    }

    public JsonLogicValidator(JsonLogicValidatorInstrumentation? instrumentation = null)
        : this([], contextEnrichers: null, options: null, instrumentation)
    {
    }

    public IReadOnlyList<ValidationEngineDescriptor> GetRegisteredEngines()
    {
        return _registeredDescriptors;
    }

    public Result<IReadOnlyList<ValidationMessage>> ValidateRulesSyntax(
        string rawLogicJson,
        out IReadOnlyList<BusinessRule>? parsedRules)
    {
        parsedRules = null;

        if (string.IsNullOrWhiteSpace(rawLogicJson))
        {
            return Result<IReadOnlyList<ValidationMessage>>.Success([
                new ValidationMessage(typeof(JsonLogicValidator), null, "Logic JSON was empty or null.", source: ValidationSources.Rule)
            ]);
        }

        List<BusinessRule> rules;
        try
        {
            var deserialized = JsonSerializer.Deserialize<List<BusinessRule>>(rawLogicJson, JsonOptions);
            if (deserialized is null || deserialized.Count == 0)
            {
                return Result<IReadOnlyList<ValidationMessage>>.Success([
                    new ValidationMessage(typeof(JsonLogicValidator), null, "Logic JSON must contain at least one business rule.", source: ValidationSources.Rule)
                ]);
            }
            rules = deserialized;
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ValidationMessage>>.Success([
                new ValidationMessage(typeof(JsonLogicValidator), null, $"Malformed Logic JSON syntax: {ex.Message}", source: ValidationSources.Rule)
            ]);
        }

        var syntaxErrors = new List<ValidationMessage>();

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var ruleId = string.IsNullOrWhiteSpace(rule.Id) ? $"Rule[{i}]" : rule.Id;

            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, "'id' is required.", source: ValidationSources.Rule, ruleId: ruleId));
            }

            if (string.IsNullOrWhiteSpace(rule.Description))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, "'description' is required.", source: ValidationSources.Rule, ruleId: ruleId));
            }

            if (string.IsNullOrWhiteSpace(rule.Path))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, "'path' target is required.", source: ValidationSources.Rule, ruleId: ruleId));
            }

            // ODCS rules use 'description' + 'implementation', whereas standalone rules use 'message' + 'assert'
            if (string.IsNullOrWhiteSpace(rule.Message) && rule.Implementation is null)
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, "'message' is required.", source: ValidationSources.Rule, ruleId: ruleId));
            }

            var engineName = rule.ResolveEngine(_options.DefaultEngine);
            if (!TryResolveEngine(engineName, out var engine, out var engineError))
            {
                if (_options.UnknownEngineBehavior == UnknownEngineBehavior.Fail)
                {
                    syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, $"{engineError}", source: ValidationSources.Rule, ruleId: ruleId));
                }
                else
                {
                    _instrumentation.EngineSkipped(ruleId, engineName);
                }
                continue;
            }

            if (rule.EffectiveAssertion is null)
            {
                syntaxErrors.Add(new ValidationMessage(typeof(JsonLogicValidator), ruleId, "'assert' (or 'implementation') logic condition is required.", source: ValidationSources.Rule, ruleId: ruleId));
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
            return Result<BusinessRuleValidationResult>.Success(new BusinessRuleValidationResult(EngineTraces: []));
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
            var traceAccumulators = new Dictionary<string, EngineTraceAccumulator>(StringComparer.OrdinalIgnoreCase);

            EngineTraceAccumulator GetOrCreateAccumulator(ValidationEngineDescriptor descriptor)
            {
                if (!traceAccumulators.TryGetValue(descriptor.EngineId, out var acc))
                {
                    acc = new EngineTraceAccumulator(descriptor);
                    traceAccumulators[descriptor.EngineId] = acc;
                }

                return acc;
            }

            // Step 1: Resolve engines via O(1) FrozenDictionary lookup
            foreach (var rule in rules)
            {
                var engineName = rule.ResolveEngine(_options.DefaultEngine);
                var isWarning = string.Equals(rule.Severity, "WARNING", StringComparison.OrdinalIgnoreCase);
                if (isWarning && !_options.TreatWarningsAsErrors)
                {
                    var warnDescriptor = ResolveDescriptorOrFallback(engineName);
                    GetOrCreateAccumulator(warnDescriptor).SkippedWarningCount++;
                    continue;
                }

                if (!TryResolveEngine(engineName, out var engine, out var engineError))
                {
                    var unknownDescriptor = ResolveDescriptorOrFallback(engineName);
                    var unknownAcc = GetOrCreateAccumulator(unknownDescriptor);

                    if (_options.UnknownEngineBehavior == UnknownEngineBehavior.Fail)
                    {
                        var path = string.IsNullOrWhiteSpace(rule.Path) ? "/" : $"/{rule.Path.Replace('.', '/')}";
                        violations.Add(new ValidationError(path, $"Rule '{rule.Id}': {engineError}", RuleId: rule.Id));
                        unknownAcc.RulesCount++;
                        unknownAcc.ViolationCount++;
                        unknownAcc.HasFailure = true;
                        unknownAcc.LastErrorSummary = engineError;
                    }
                    else
                    {
                        _instrumentation.EngineSkipped(rule.Id, engineName);
                        unknownAcc.SkippedUnknownCount++;
                        unknownAcc.LastErrorSummary ??= $"Engine '{engineName}' skipped per UnknownEngineBehavior.Skip";
                    }
                    continue;
                }

                if (rule.EffectiveAssertion is not null)
                {
                    resolvedRuleEngines.Add((rule, engine!));
                    GetOrCreateAccumulator(engine!.Descriptor).RulesCount++;
                }
            }

            if (resolvedRuleEngines.Count > 0)
            {
                var ambientContext = RuleTargetResolver.BuildAmbientContext(rootNode, _contextEnrichers);

                // Step 2: Group rules by target Path so each path in the JSON tree is traversed and enriched once
                foreach (var pathGroup in resolvedRuleEngines.GroupBy(x => x.Rule.Path ?? string.Empty, StringComparer.Ordinal))
                {
                    var targets = RuleTargetResolver.ResolvePathTargets(rootNode, pathGroup.Key);

                    foreach (var target in targets)
                    {
                        var enrichedNode = RuleTargetResolver.CreateEnrichedContextNode(
                            target.ContextNode,
                            target.ChildFocusNode,
                            rootNode,
                            ambientContext,
                            _contextEnrichers);

                        foreach (var (rule, engine) in pathGroup)
                        {
                            var acc = GetOrCreateAccumulator(engine.Descriptor);
                            var evalStart = Stopwatch.GetTimestamp();
                            var evalOutcome = engine.Evaluate(rule, enrichedNode, rootNode);
                            acc.ElapsedTicks += Stopwatch.GetTimestamp() - evalStart;

                            if (evalOutcome.IsFailure)
                            {
                                var errPath = string.IsNullOrWhiteSpace(target.InstancePath) ? rule.Path : target.InstancePath;
                                var errMsg = evalOutcome.Error.Description ?? $"Rule '{rule.Id}' evaluation failed.";
                                violations.Add(new ValidationError(errPath, errMsg, RuleId: rule.Id));
                                acc.ViolationCount++;
                                acc.HasFailure = true;
                                acc.LastErrorSummary ??= errMsg;
                                continue;
                            }

                            if (!evalOutcome.Value)
                            {
                                var failurePath = string.IsNullOrWhiteSpace(target.InstancePath) ? rule.Path : target.InstancePath;
                                _instrumentation.RuleViolation(rule.Id, engine.EngineName, failurePath, rule.EffectiveMessage);
                                violations.Add(new ValidationError(failurePath, rule.EffectiveMessage, RuleId: rule.Id));
                                acc.ViolationCount++;
                            }
                        }
                    }
                }
            }

            var engineTraces = traceAccumulators.Values
                .Select(acc => acc.ToTrace())
                .ToArray();

            if (violations.Count > 0)
            {
                _instrumentation.ValidationFailed(violations.Count);
                return new BusinessRuleValidationResult(violations, engineTraces);
            }

            _instrumentation.ValidationPassed();
            return new BusinessRuleValidationResult(EngineTraces: engineTraces);
        }
        catch (Exception ex)
        {
            _instrumentation.ValidationMalformedJson(ex.Message);
            return Error.Validation(ErrorCodes.JsonMalformed, new { ex.Message });
        }
    }

    private ValidationEngineDescriptor ResolveDescriptorOrFallback(string engineName)
    {
        if (_engines.TryGetValue(engineName, out var registered))
        {
            return registered.Descriptor;
        }

        return new ValidationEngineDescriptor(
            EngineId: engineName,
            DisplayName: engineName,
            Kind: ValidationEngineKinds.QualityRule,
            Description: $"Unregistered or unsupported quality rule engine '{engineName}'.");
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

    private sealed class EngineTraceAccumulator(ValidationEngineDescriptor descriptor)
    {
        public ValidationEngineDescriptor Descriptor { get; } = descriptor;
        public int RulesCount { get; set; }
        public int SkippedWarningCount { get; set; }
        public int SkippedUnknownCount { get; set; }
        public int ViolationCount { get; set; }
        public long ElapsedTicks { get; set; }
        public bool HasFailure { get; set; }
        public string? LastErrorSummary { get; set; }

        public ValidationEngineTrace ToTrace()
        {
            var durationMs = (long)Stopwatch.GetElapsedTime(0, ElapsedTicks).TotalMilliseconds;

            if (RulesCount == 0 && (SkippedWarningCount > 0 || SkippedUnknownCount > 0))
            {
                var skippedCount = SkippedWarningCount + SkippedUnknownCount;
                return new ValidationEngineTrace(
                    Descriptor,
                    nameof(SubmissionProcessOutcome.Skipped),
                    ItemsEvaluated: skippedCount,
                    ViolationCount: 0,
                    DurationMs: durationMs,
                    Summary: LastErrorSummary ?? $"Skipped {skippedCount} rule(s)");
            }

            string outcome;
            if (HasFailure)
            {
                outcome = nameof(SubmissionProcessOutcome.Failed);
            }
            else if (ViolationCount > 0)
            {
                outcome = nameof(SubmissionProcessOutcome.ValidationErrorsFound);
            }
            else
            {
                outcome = nameof(SubmissionProcessOutcome.Succeeded);
            }

            var summary = ViolationCount == 0
                ? $"Evaluated {RulesCount} rule(s) with 0 violations"
                : LastErrorSummary is not null && HasFailure
                    ? LastErrorSummary
                    : $"Evaluated {RulesCount} rule(s); {ViolationCount} violation(s)";

            return new ValidationEngineTrace(
                Descriptor,
                outcome,
                ItemsEvaluated: RulesCount,
                ViolationCount: ViolationCount,
                DurationMs: durationMs,
                Summary: summary);
        }
    }
}
