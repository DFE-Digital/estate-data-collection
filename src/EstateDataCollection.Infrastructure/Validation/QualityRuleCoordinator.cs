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

public class QualityRuleCoordinator : IBusinessLogicValidator
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
        string rawLogicJson,
        out IReadOnlyList<BusinessRule>? parsedRules)
    {
        parsedRules = null;

        if (string.IsNullOrWhiteSpace(rawLogicJson))
        {
            return Result<IReadOnlyList<ValidationMessage>>.Success([
                new ValidationMessage(typeof(QualityRuleCoordinator), null, "Logic JSON was empty or null.", source: ValidationSources.Rule)
            ]);
        }

        List<BusinessRule> rules;
        try
        {
            var deserialized = JsonSerializer.Deserialize<List<BusinessRule>>(rawLogicJson, JsonOptions);
            if (deserialized is null || deserialized.Count == 0)
            {
                return Result<IReadOnlyList<ValidationMessage>>.Success([
                    new ValidationMessage(typeof(QualityRuleCoordinator), null, "Logic JSON must contain at least one business rule.", source: ValidationSources.Rule)
                ]);
            }
            rules = deserialized;
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ValidationMessage>>.Success([
                new ValidationMessage(typeof(QualityRuleCoordinator), null, $"Malformed Logic JSON syntax: {ex.Message}", source: ValidationSources.Rule)
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
            // Advisory Info rules may omit implementation/assert when Description or Message is provided.
            if (string.IsNullOrWhiteSpace(rule.Message) &&
                rule.Implementation is null &&
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

            if (rule.EffectiveAssertion is null)
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
            return Result<BusinessRuleValidationResult>.Success(
                new BusinessRuleValidationResult(EngineTraces: [], Issues: ValidationIssuesHelper.EmptyIssues));
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

                if (rule.EffectiveAssertion is not null || rule.Severity == RuleSeverity.Info)
                {
                    resolvedRules.Add(new ResolvedRuleEntry(rule, engine!, i));
                    _ = GetOrCreateAccumulator(engine!.Descriptor);
                }
            }

            if (resolvedRules.Count > 0)
            {
                // Step 2: Topological & runbook sort by (Stage, Order) honoring DependsOn
                var orderedRules = SortRulesForRunbook(resolvedRules);
                var ambientContext = RuleTargetResolver.BuildAmbientContext(rootNode, _contextEnrichers);
                var pathTargetCache = new Dictionary<string, IReadOnlyList<(RuleTargetResolver.ResolvedRuleTarget Target, JsonNode EnrichedNode)>>(StringComparer.Ordinal);
                var failedRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int? haltedAfterStage = null;

                IReadOnlyList<(RuleTargetResolver.ResolvedRuleTarget Target, JsonNode EnrichedNode)> GetOrResolvePathTargets(string path)
                {
                    if (pathTargetCache.TryGetValue(path, out var cached))
                    {
                        return cached;
                    }

                    var targets = RuleTargetResolver.ResolvePathTargets(rootNode, path);
                    var prepared = new List<(RuleTargetResolver.ResolvedRuleTarget, JsonNode)>(targets.Count);
                    foreach (var target in targets)
                    {
                        var enriched = RuleTargetResolver.CreateEnrichedContextNode(
                            target.ContextNode,
                            target.ChildFocusNode,
                            rootNode,
                            ambientContext,
                            _contextEnrichers);
                        prepared.Add((target, enriched));
                    }

                    pathTargetCache[path] = prepared;
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

                    var pathTargets = GetOrResolvePathTargets(rule.Path ?? string.Empty);
                    acc.RulesEvaluatedCount++;
                    var ruleFailed = false;

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

    private static IReadOnlyList<ResolvedRuleEntry> SortRulesForRunbook(IReadOnlyList<ResolvedRuleEntry> entries)
    {
        if (entries.Count <= 1)
        {
            return entries;
        }

        var idToIndices = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            var id = entries[i].Rule.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!idToIndices.TryGetValue(id, out var list))
            {
                list = [];
                idToIndices[id] = list;
            }
            list.Add(i);
        }

        var dependents = new List<int>[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            dependents[i] = [];
        }

        var inDegree = new int[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var deps = entries[i].Rule.DependsOn;
            if (deps is not { Count: > 0 })
            {
                continue;
            }

            var seenPrereqs = new HashSet<int>();
            foreach (var depId in deps)
            {
                if (string.IsNullOrWhiteSpace(depId) || !idToIndices.TryGetValue(depId.Trim(), out var prereqIndices))
                {
                    continue;
                }

                foreach (var prereqIdx in prereqIndices)
                {
                    if (prereqIdx == i || !seenPrereqs.Add(prereqIdx))
                    {
                        continue;
                    }

                    dependents[prereqIdx].Add(i);
                    inDegree[i]++;
                }
            }
        }

        var queue = new PriorityQueue<int, (int Stage, int Order, int OriginalIndex)>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (inDegree[i] == 0)
            {
                var r = entries[i].Rule;
                queue.Enqueue(i, (r.Stage, r.Order, entries[i].OriginalIndex));
            }
        }

        var sorted = new List<ResolvedRuleEntry>(entries.Count);
        var visited = new bool[entries.Count];

        while (queue.TryDequeue(out var idx, out _))
        {
            if (visited[idx])
            {
                continue;
            }

            visited[idx] = true;
            sorted.Add(entries[idx]);

            foreach (var depIdx in dependents[idx])
            {
                inDegree[depIdx]--;
                if (inDegree[depIdx] == 0)
                {
                    var depRule = entries[depIdx].Rule;
                    queue.Enqueue(depIdx, (depRule.Stage, depRule.Order, entries[depIdx].OriginalIndex));
                }
            }
        }

        if (sorted.Count < entries.Count)
        {
            var remaining = new List<ResolvedRuleEntry>(entries.Count - sorted.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                if (!visited[i])
                {
                    remaining.Add(entries[i]);
                }
            }

            remaining.Sort((a, b) =>
            {
                var cmp = a.Rule.Stage.CompareTo(b.Rule.Stage);
                if (cmp != 0) return cmp;
                cmp = a.Rule.Order.CompareTo(b.Rule.Order);
                return cmp != 0 ? cmp : a.OriginalIndex.CompareTo(b.OriginalIndex);
            });

            sorted.AddRange(remaining);
        }

        return sorted;
    }

    private static bool HasFailedDependency(
        BusinessRule rule,
        HashSet<string> failedRuleIds,
        out string? failedDependencyId)
    {
        if (rule.DependsOn is { Count: > 0 } && failedRuleIds.Count > 0)
        {
            foreach (var dep in rule.DependsOn)
            {
                if (!string.IsNullOrWhiteSpace(dep) && failedRuleIds.Contains(dep.Trim()))
                {
                    failedDependencyId = dep.Trim();
                    return true;
                }
            }
        }

        failedDependencyId = null;
        return false;
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

    private sealed record ResolvedRuleEntry(BusinessRule Rule, IQualityRuleEngine Engine, int OriginalIndex);

    private sealed class EngineTraceAccumulator(ValidationEngineDescriptor descriptor)
    {
        public ValidationEngineDescriptor Descriptor { get; } = descriptor;
        public int RulesEvaluatedCount { get; set; }
        public int SkippedUnknownCount { get; set; }
        public int SkippedRunbookCount { get; set; }
        public int ViolationCount { get; set; }
        public int AdvisoryInfoCount { get; set; }
        public long ElapsedTicks { get; set; }
        public bool HasFailure { get; set; }
        public string? LastErrorSummary { get; set; }

        public ValidationEngineTrace ToTrace()
        {
            var durationMs = (long)Stopwatch.GetElapsedTime(0, ElapsedTicks).TotalMilliseconds;
            var totalSkipped = SkippedUnknownCount + SkippedRunbookCount;

            if (RulesEvaluatedCount == 0 && totalSkipped > 0)
            {
                return new ValidationEngineTrace(
                    Descriptor,
                    nameof(SubmissionProcessOutcome.Skipped),
                    ItemsEvaluated: totalSkipped,
                    ViolationCount: 0,
                    DurationMs: durationMs,
                    Summary: LastErrorSummary ?? $"Skipped {totalSkipped} rule(s)");
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

            var skipSuffix = totalSkipped > 0 ? $"; {totalSkipped} skipped" : string.Empty;
            var infoSuffix = AdvisoryInfoCount > 0 ? $"; {AdvisoryInfoCount} info notice(s)" : string.Empty;

            var summary = ViolationCount == 0
                ? $"Evaluated {RulesEvaluatedCount} rule(s) with 0 violations{skipSuffix}{infoSuffix}"
                : LastErrorSummary is not null && HasFailure
                    ? LastErrorSummary
                    : $"Evaluated {RulesEvaluatedCount} rule(s); {ViolationCount} violation(s){skipSuffix}{infoSuffix}";

            return new ValidationEngineTrace(
                Descriptor,
                outcome,
                ItemsEvaluated: RulesEvaluatedCount,
                ViolationCount: ViolationCount,
                DurationMs: durationMs,
                Summary: summary);
        }
    }
}
