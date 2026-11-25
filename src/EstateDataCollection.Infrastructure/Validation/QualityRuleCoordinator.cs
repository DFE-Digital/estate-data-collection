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

public partial class QualityRuleCoordinator : IBusinessLogicValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly FrozenDictionary<string, IQualityRuleEngine> _engines;
    private readonly IReadOnlyList<ValidationEngineDescriptor> _registeredDescriptors;
    private readonly FrozenSet<string> _enabledEngines;
    private readonly IReadOnlyList<IRuleContextEnricher> _contextEnrichers;
    private readonly ValidationOptions _options;
    private readonly QualityRuleCoordinatorInstrumentation _instrumentation;

    [ActivatorUtilitiesConstructor]
    public QualityRuleCoordinator(
        IEnumerable<IQualityRuleEngine> engines,
        IEnumerable<IRuleContextEnricher>? contextEnrichers,
        IOptions<ValidationOptions>? options = null,
        QualityRuleCoordinatorInstrumentation? instrumentation = null)
    {
        _options = options?.Value ?? new ValidationOptions();
        _instrumentation = instrumentation ?? new QualityRuleCoordinatorInstrumentation(NullLogger<QualityRuleCoordinator>.Instance);

        var engineList = engines?.ToList() ?? [];
        if (engineList.Count == 0)
        {
            engineList =
            [
                new JsonLogicQualityEngine(options),
                new CelQualityEngine(options),
                new UniqueCombinationQualityEngine()
            ];
        }

        _engines = engineList.ToFrozenDictionary(
            e => e.EngineName,
            e => e,
            StringComparer.OrdinalIgnoreCase);

        _registeredDescriptors = engineList
            .Select(e => e.Descriptor)
            .ToArray();

        _enabledEngines = (_options.EnabledEngines ?? ["jsonlogic", "cel", "unique"])
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        _contextEnrichers = contextEnrichers is null
            ? [new EstateAmbientReferenceEnricher(), new SurveyAnswerContextEnricher()]
            : contextEnrichers.ToArray();
    }

    public QualityRuleCoordinator(
        IEnumerable<IQualityRuleEngine> engines,
        IOptions<ValidationOptions>? options = null,
        QualityRuleCoordinatorInstrumentation? instrumentation = null)
        : this(engines, contextEnrichers: null, options, instrumentation)
    {
    }

    public QualityRuleCoordinator(QualityRuleCoordinatorInstrumentation? instrumentation = null)
        : this([], contextEnrichers: null, options: null, instrumentation)
    {
    }

    public IReadOnlyList<ValidationEngineDescriptor> GetRegisteredEngines()
    {
        return _registeredDescriptors;
    }

    public Result<IReadOnlyList<ValidationMessage>> ValidateRulesSyntax(
        string rawRulesJson,
        out IReadOnlyList<BusinessRule>? parsedRules)
    {
        parsedRules = null;

        if (string.IsNullOrWhiteSpace(rawRulesJson))
        {
            return Result<IReadOnlyList<ValidationMessage>>.Success([
                new ValidationMessage(typeof(QualityRuleCoordinator), null, "Rules JSON was empty or null.", source: ValidationSources.Rule)
            ]);
        }

        List<BusinessRule> rules;
        try
        {
            var deserialized = JsonSerializer.Deserialize<List<BusinessRule>>(rawRulesJson, JsonOptions);
            if (deserialized is null || deserialized.Count == 0)
            {
                return Result<IReadOnlyList<ValidationMessage>>.Success([
                    new ValidationMessage(typeof(QualityRuleCoordinator), null, "Rules JSON must contain at least one business rule.", source: ValidationSources.Rule)
                ]);
            }
            rules = deserialized;
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ValidationMessage>>.Success([
                new ValidationMessage(typeof(QualityRuleCoordinator), null, $"Malformed Rules JSON syntax: {ex.Message}", source: ValidationSources.Rule)
            ]);
        }

        var syntaxErrors = new List<ValidationMessage>();

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var ruleId = string.IsNullOrWhiteSpace(rule.Id) ? $"Rule[{i}]" : rule.Id;

            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(QualityRuleCoordinator), ruleId, "'id' is required.", source: ValidationSources.Rule, ruleId: ruleId));
            }

            if (string.IsNullOrWhiteSpace(rule.Description))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(QualityRuleCoordinator), ruleId, "'description' is required.", source: ValidationSources.Rule, ruleId: ruleId));
            }

            if (string.IsNullOrWhiteSpace(rule.Path))
            {
                syntaxErrors.Add(new ValidationMessage(typeof(QualityRuleCoordinator), ruleId, "'path' target is required.", source: ValidationSources.Rule, ruleId: ruleId));
            }

            // ODCS rules use 'description' + 'implementation', whereas standalone rules use 'message' + 'assert'.
            // Unique rules may specify 'fields', and advisory Info rules may omit implementation/assert.
            if (string.IsNullOrWhiteSpace(rule.Message) &&
                rule.Implementation is null &&
                rule.Fields is not { Count: > 0 } &&
                rule.Severity != RuleSeverity.Info)
            {
                syntaxErrors.Add(new ValidationMessage(typeof(QualityRuleCoordinator), ruleId, "'message' is required.", source: ValidationSources.Rule, ruleId: ruleId));
            }

            var engineName = rule.ResolveEngine(_options.DefaultEngine);
            if (!TryResolveEngine(engineName, out var engine, out var engineError))
            {
                if (_options.UnknownEngineBehavior == UnknownEngineBehavior.Fail)
                {
                    syntaxErrors.Add(new ValidationMessage(typeof(QualityRuleCoordinator), ruleId, $"{engineError}", source: ValidationSources.Rule, ruleId: ruleId));
                }
                else
                {
                    _instrumentation.EngineSkipped(ruleId, engineName);
                }
                continue;
            }

            var isBatchOrFieldsRule = engine is IBatchQualityRuleEngine || rule.Fields is { Count: > 0 };
            if (rule.EffectiveAssertion is null && !isBatchOrFieldsRule)
            {
                if (rule.Severity != RuleSeverity.Info)
                {
                    syntaxErrors.Add(new ValidationMessage(typeof(QualityRuleCoordinator), ruleId, "'assert' (or 'implementation') logic condition is required.", source: ValidationSources.Rule, ruleId: ruleId));
                }
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

    public Task<Result<BusinessRuleValidationResult>> ValidateAsync(
        string payloadJson,
        IReadOnlyList<BusinessRule> rules,
        CancellationToken cancellationToken = default)
    {
        return ValidateAsync(
            payloadJson,
            rules,
            additionalAmbientContext: null,
            numericToleranceOverride: null,
            cancellationToken);
    }

    public async Task<Result<BusinessRuleValidationResult>> ValidateAsync(
        string payloadJson,
        IReadOnlyList<BusinessRule> rules,
        IReadOnlyDictionary<string, JsonNode?>? additionalAmbientContext,
        double? numericToleranceOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return Error.Validation(ErrorCodes.JsonEmptyPayload);
        }

        if (rules is null || rules.Count == 0)
        {
            return Result<BusinessRuleValidationResult>.Success(
                new BusinessRuleValidationResult(EngineTraces: [], Issues: ValidationIssuesHelper.EmptyIssues));
        }

        using var toleranceScope = numericToleranceOverride.HasValue
            ? NumericToleranceScope.Push(numericToleranceOverride.Value)
            : null;

        try
        {
            _instrumentation.ValidationStarted(rules.Count);

            var rootNode = JsonNode.Parse(payloadJson);
            if (rootNode is null)
            {
                return Error.Validation(ErrorCodes.JsonEmptyPayload);
            }

            var violations = new List<ValidationError>();
            var resolvedRules = new List<ResolvedRuleEntry>(rules.Count);
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

            // Step 1: Resolve engines across ALL severities (Critical, Error, Warning, Info)
            for (var i = 0; i < rules.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rule = rules[i];
                var engineName = rule.ResolveEngine(_options.DefaultEngine);

                if (!TryResolveEngine(engineName, out var engine, out var engineError))
                {
                    var unknownDescriptor = ResolveDescriptorOrFallback(engineName);
                    var unknownAcc = GetOrCreateAccumulator(unknownDescriptor);

                    if (_options.UnknownEngineBehavior == UnknownEngineBehavior.Fail)
                    {
                        var path = string.IsNullOrWhiteSpace(rule.Path) ? "/" : $"/{rule.Path.Replace('.', '/')}";
                        violations.Add(new ValidationError(
                            PropertyPath: path,
                            Message: $"Rule '{rule.Id}': {engineError}",
                            RuleId: rule.Id,
                            Severity: rule.Severity,
                            Action: rule.EffectiveAction,
                            EngineId: engineName));
                        unknownAcc.RulesEvaluatedCount++;
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

                if (rule.EffectiveAssertion is not null ||
                    rule.Fields is { Count: > 0 } ||
                    engine is IBatchQualityRuleEngine ||
                    rule.Severity == RuleSeverity.Info)
                {
                    resolvedRules.Add(new ResolvedRuleEntry(rule, engine!, i));
                    _ = GetOrCreateAccumulator(engine!.Descriptor);
                }
            }

            if (resolvedRules.Count > 0)
            {
                // Step 2: Topological & runbook sort by (Stage, Order) honoring DependsOn
                var orderedRules = SortRulesForRunbook(resolvedRules);
                var ambientContext = BuildMergedAmbientContext(rootNode, additionalAmbientContext);
                var rawTargetCache = new Dictionary<string, IReadOnlyList<RuleTargetResolver.ResolvedRuleTarget>>(StringComparer.Ordinal);
                var enrichedTargetCache = new Dictionary<string, IReadOnlyList<(RuleTargetResolver.ResolvedRuleTarget Target, JsonNode EnrichedNode)>>(StringComparer.Ordinal);
                var failedRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int? haltedAfterStage = null;

                IReadOnlyList<RuleTargetResolver.ResolvedRuleTarget> GetOrResolveRawTargets(string path)
                {
                    if (rawTargetCache.TryGetValue(path, out var cachedRaw))
                    {
                        return cachedRaw;
                    }

                    var targets = RuleTargetResolver.ResolvePathTargets(rootNode, path);
                    rawTargetCache[path] = targets;
                    return targets;
                }

                IReadOnlyList<(RuleTargetResolver.ResolvedRuleTarget Target, JsonNode EnrichedNode)> GetOrResolveEnrichedTargets(string path)
                {
                    if (enrichedTargetCache.TryGetValue(path, out var cached))
                    {
                        return cached;
                    }

                    var targets = GetOrResolveRawTargets(path);
                    var prepared = new List<(RuleTargetResolver.ResolvedRuleTarget, JsonNode)>(targets.Count);
                    foreach (var target in targets)
                    {
                        var enriched = RuleTargetResolver.CreateEnrichedContextNode(
                            target.ContextNode,
                            target.ChildFocusNode,
                            rootNode,
                            ambientContext,
                            _contextEnrichers,
                            target.Ancestors,
                            target.Index);
                        prepared.Add((target, enriched));
                    }

                    enrichedTargetCache[path] = prepared;
                    return prepared;
                }

                // Step 3: Evaluate ordered rules with dependency-skipping and critical/stop-on-failure stage halting
                foreach (var entry in orderedRules)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rule = entry.Rule;
                    var engine = entry.Engine;
                    var acc = GetOrCreateAccumulator(engine.Descriptor);

                    if (haltedAfterStage.HasValue && rule.Stage > haltedAfterStage.Value)
                    {
                        const string haltReason = "Skipped because an earlier stage halted on a Critical/StopOnFailure violation";
                        acc.SkippedRunbookCount++;
                        acc.LastErrorSummary ??= haltReason;
                        _instrumentation.RuleSkippedByRunbook(rule.Id, haltReason);
                        continue;
                    }

                    if (HasFailedDependency(rule, failedRuleIds, out var failedDependencyId))
                    {
                        var depReason = $"Skipped because upstream dependency '{failedDependencyId}' failed";
                        acc.SkippedRunbookCount++;
                        acc.LastErrorSummary ??= depReason;
                        _instrumentation.RuleSkippedByRunbook(rule.Id, depReason);
                        continue;
                    }

                    acc.RulesEvaluatedCount++;
                    var ruleFailed = false;

                    if (engine is IBatchQualityRuleEngine batchEngine &&
                        (rule.Severity != RuleSeverity.Info || rule.EffectiveAssertion is not null || rule.Fields is { Count: > 0 }))
                    {
                        var rawTargets = GetOrResolveRawTargets(rule.Path ?? string.Empty);
                        var batchItems = new List<ResolvedTargetItem>(rawTargets.Count);
                        foreach (var target in rawTargets)
                        {
                            var targetPath = string.IsNullOrWhiteSpace(target.InstancePath) ? (rule.Path ?? "/") : target.InstancePath;
                            batchItems.Add(new ResolvedTargetItem(
                                targetPath,
                                target.ContextNode,
                                target.ChildFocusNode,
                                target.Ancestors,
                                target.Index,
                                ambientContext));
                        }

                        var batchStart = Stopwatch.GetTimestamp();
                        var batchOutcome = batchEngine.EvaluateBatch(rule, batchItems, rootNode);
                        acc.ElapsedTicks += Stopwatch.GetTimestamp() - batchStart;

                        if (batchOutcome.IsFailure)
                        {
                            var errMsg = batchOutcome.Error.Description ?? $"Rule '{rule.Id}' evaluation failed.";
                            var fallbackPath = batchItems.Count > 0 ? batchItems[0].TargetPath : (rule.Path ?? "/");
                            violations.Add(new ValidationError(
                                PropertyPath: fallbackPath,
                                Message: errMsg,
                                RuleId: rule.Id,
                                Severity: rule.Severity,
                                Action: rule.EffectiveAction,
                                EngineId: engine.EngineName));
                            acc.ViolationCount++;
                            acc.HasFailure = true;
                            acc.LastErrorSummary ??= errMsg;
                            ruleFailed = true;
                        }
                        else if (batchOutcome.Value.Count > 0)
                        {
                            foreach (var violation in batchOutcome.Value)
                            {
                                var msg = !string.IsNullOrWhiteSpace(violation.Message)
                                    ? violation.Message!
                                    : rule.EffectiveMessage;
                                _instrumentation.RuleViolation(
                                    rule.Id,
                                    engine.EngineName,
                                    rule.Severity.ToKeyString(),
                                    violation.TargetPath,
                                    msg);
                                violations.Add(new ValidationError(
                                    PropertyPath: violation.TargetPath,
                                    Message: msg,
                                    RuleId: rule.Id,
                                    Severity: rule.Severity,
                                    Action: rule.EffectiveAction,
                                    EngineId: engine.EngineName));
                                acc.ViolationCount++;
                                ruleFailed = true;
                            }
                        }
                    }
                    else
                    {
                        var pathTargets = GetOrResolveEnrichedTargets(rule.Path ?? string.Empty);
                        foreach (var (target, enrichedNode) in pathTargets)
                        {
                            var targetPath = string.IsNullOrWhiteSpace(target.InstancePath) ? (rule.Path ?? "/") : target.InstancePath;

                            // Advisory Info rule without assertion: emit advisory notice directly at target path
                            if (rule.EffectiveAssertion is null)
                            {
                                violations.Add(new ValidationError(
                                    PropertyPath: targetPath,
                                    Message: rule.EffectiveMessage,
                                    RuleId: rule.Id,
                                    Severity: RuleSeverity.Info,
                                    Action: rule.EffectiveAction,
                                    EngineId: engine.EngineName));
                                acc.AdvisoryInfoCount++;
                                continue;
                            }

                            var evalStart = Stopwatch.GetTimestamp();
                            var evalOutcome = engine.Evaluate(rule, enrichedNode, rootNode);
                            acc.ElapsedTicks += Stopwatch.GetTimestamp() - evalStart;

                            if (evalOutcome.IsFailure)
                            {
                                var errMsg = evalOutcome.Error.Description ?? $"Rule '{rule.Id}' evaluation failed.";
                                violations.Add(new ValidationError(
                                    PropertyPath: targetPath,
                                    Message: errMsg,
                                    RuleId: rule.Id,
                                    Severity: rule.Severity,
                                    Action: rule.EffectiveAction,
                                    EngineId: engine.EngineName));
                                acc.ViolationCount++;
                                acc.HasFailure = true;
                                acc.LastErrorSummary ??= errMsg;
                                ruleFailed = true;
                                continue;
                            }

                            if (!evalOutcome.Value)
                            {
                                _instrumentation.RuleViolation(
                                    rule.Id,
                                    engine.EngineName,
                                    rule.Severity.ToKeyString(),
                                    targetPath,
                                    rule.EffectiveMessage);
                                violations.Add(new ValidationError(
                                    PropertyPath: targetPath,
                                    Message: rule.EffectiveMessage,
                                    RuleId: rule.Id,
                                    Severity: rule.Severity,
                                    Action: rule.EffectiveAction,
                                    EngineId: engine.EngineName));
                                acc.ViolationCount++;
                                ruleFailed = true;
                            }
                        }
                    }

                    if (ruleFailed)
                    {
                        if (!string.IsNullOrWhiteSpace(rule.Id))
                        {
                            failedRuleIds.Add(rule.Id);
                        }

                        if (rule.EffectiveStopOnFailure)
                        {
                            haltedAfterStage ??= rule.Stage;
                        }
                    }
                }
            }

            var engineTraces = traceAccumulators.Values
                .Select(acc => acc.ToTrace())
                .ToArray();
            var groupedIssues = ValidationIssuesHelper.GroupBySeverity(violations);

            if (violations.Count > 0)
            {
                _instrumentation.ValidationFailed(violations.Count);
                return new BusinessRuleValidationResult(violations, engineTraces, groupedIssues);
            }

            _instrumentation.ValidationPassed();
            return new BusinessRuleValidationResult(
                Errors: [],
                EngineTraces: engineTraces,
                Issues: groupedIssues);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _instrumentation.ValidationMalformedJson(ex.Message);
            return Error.Validation(ErrorCodes.JsonMalformed, new { ex.Message });
        }
    }

    private IReadOnlyDictionary<string, JsonNode?> BuildMergedAmbientContext(
        JsonNode rootNode,
        IReadOnlyDictionary<string, JsonNode?>? additionalAmbientContext)
    {
        var baseAmbient = RuleTargetResolver.BuildAmbientContext(rootNode, _contextEnrichers);
        var merged = new Dictionary<string, JsonNode?>(baseAmbient, StringComparer.Ordinal);
        if (additionalAmbientContext is not null)
        {
            foreach (var (key, value) in additionalAmbientContext)
            {
                merged[key] = value?.DeepClone();
            }
        }

        var ambientRoot = merged.TryGetValue("ambient", out var existingAmbient) && existingAmbient is JsonObject existingObj
            ? existingObj.DeepClone().AsObject()
            : new JsonObject();

        if (merged.TryGetValue("reference", out var refNode) && refNode is not null && !ambientRoot.ContainsKey("reference"))
        {
            ambientRoot["reference"] = refNode.DeepClone();
        }
        else if (ambientRoot.TryGetPropertyValue("reference", out var nestedRef) && nestedRef is not null && !merged.ContainsKey("reference"))
        {
            merged["reference"] = nestedRef.DeepClone();
        }

        if (merged.TryGetValue("cross_submission", out var crossNode) && crossNode is not null && !ambientRoot.ContainsKey("cross_submission"))
        {
            ambientRoot["cross_submission"] = crossNode.DeepClone();
        }
        else if (ambientRoot.TryGetPropertyValue("cross_submission", out var nestedCross) && nestedCross is not null && !merged.ContainsKey("cross_submission"))
        {
            merged["cross_submission"] = nestedCross.DeepClone();
        }

        if (ambientRoot.Count > 0)
        {
            merged["ambient"] = ambientRoot;
        }

        return merged;
    }
}
