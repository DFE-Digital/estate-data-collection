using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using Json.Schema;

namespace EstateDataCollection.Application.Validators;

public sealed partial class SchemaVerificationService
{
    private async Task EvaluateValidFixtureAsync(
        string payload,
        string fixtureId,
        JsonSchema? candidateSchema,
        IReadOnlyList<BusinessRule>? candidateRules,
        IdentifierSchemaAlignmentResult identifierAlignment,
        string effectiveIdentifierType,
        SchemaValidationPolicy effectivePolicy,
        EvaluationOptions evalOptions,
        IReadOnlyDictionary<string, JsonNode?>? ambientFixtures,
        EngineRunState schemaTrace,
        EngineRunState identifierTrace,
        IEnumerable<EngineRunState> allRuleStates,
        Func<string, EngineRunState> getOrCreateRuleTrace,
        List<ValidationMessage> fixtureErrors,
        List<ValidationMessage> warnings,
        List<ValidationError> additionalIssues,
        CancellationToken cancellationToken)
    {
        var payloadParsed = true;

        if (candidateSchema is not null)
        {
            var schemaFixtureStart = Stopwatch.GetTimestamp();
            schemaTrace.ItemsEvaluated++;
            try
            {
                using var validDoc = JsonDocument.Parse(payload);
                var eval = candidateSchema.Evaluate(validDoc.RootElement, evalOptions);

                if (!eval.IsValid)
                {
                    schemaTrace.ViolationCount++;
                    schemaTrace.FirstIssue ??= $"{fixtureId} rejected by schema.";

                    fixtureErrors.Add(ValidationMessage.For(
                        ValidationSources.Schema,
                        $"{fixtureId} was expected to pass, but was rejected by the schema.",
                        identifier: fixtureId,
                        engineType: typeof(JsonSchema)));

                    CollectSchemaErrors(eval, fixtureId, fixtureErrors, addFallbackIfEmpty: false, prefixMessageWithIdentifier: true);
                }
            }
            catch (Exception ex)
            {
                payloadParsed = false;
                schemaTrace.ViolationCount++;
                schemaTrace.FirstIssue ??= $"{fixtureId} could not be parsed: {ex.Message}";

                fixtureErrors.Add(ValidationMessage.For(
                    ValidationSources.Json,
                    $"{fixtureId} could not be parsed: {ex.Message}",
                    identifier: fixtureId,
                    engineType: typeof(JsonSchema)));
            }
            finally
            {
                schemaTrace.ElapsedTicks += Stopwatch.GetTimestamp() - schemaFixtureStart;
            }
        }

        if (candidateRules is { Count: > 0 })
        {
            var rulesStart = Stopwatch.GetTimestamp();
            var logicResult = await logicValidator.ValidateAsync(
                payload,
                candidateRules,
                ambientFixtures,
                (double?)effectivePolicy.NumericToleranceOverride,
                cancellationToken);
            var rulesTicks = Stopwatch.GetTimestamp() - rulesStart;

            if (logicResult.IsFailure)
            {
                foreach (var state in allRuleStates.Where(s => s.RuleCount > 0))
                {
                    state.HasFailure = true;
                    state.ViolationCount++;
                    state.FirstIssue ??= logicResult.Error.Description ?? logicResult.Error.Code;
                    state.ElapsedTicks += rulesTicks;
                }

                fixtureErrors.Add(ValidationMessage.For(
                    ValidationSources.Rule,
                    $"{fixtureId} quality rule evaluation failed: {logicResult.Error.Description}",
                    identifier: fixtureId,
                    engineType: logicValidator.GetType(),
                    metadata: new Dictionary<string, object?> { ["errorCode"] = logicResult.Error.Code }));
            }
            else
            {
                MergeRuleEngineTracesForValidFixture(
                    logicResult.Value,
                    candidateRules,
                    rulesTicks,
                    getOrCreateRuleTrace);

                var allRuleIssues = logicResult.Value.Errors ?? [];
                var blockingRuleIssues = ValidationIssuesHelper.GetBlockingIssues(allRuleIssues, effectivePolicy.FailOnMinimumSeverity);
                if (blockingRuleIssues.Count > 0)
                {
                    additionalIssues.AddRange(blockingRuleIssues);
                    var ruleErrors = string.Join("; ", blockingRuleIssues.Select(e => $"{e.PropertyPath}: {e.Message}"));
                    var ruleIds = blockingRuleIssues.Select(e => e.RuleId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
                    var metaDict = new Dictionary<string, object?> { ["capturedInIssues"] = true };
                    if (ruleIds.Length > 0) metaDict["ruleIds"] = ruleIds;
                    fixtureErrors.Add(ValidationMessage.For(
                        ValidationSources.Rule,
                        $"{fixtureId} violated quality rules: {ruleErrors}",
                        identifier: fixtureId,
                        ruleId: ruleIds.Length == 1 ? ruleIds[0] : null,
                        engineType: logicValidator.GetType(),
                        metadata: metaDict));
                }

                foreach (var nonBlocking in allRuleIssues.Where(e => !e.Severity.IsAtOrAbove(effectivePolicy.FailOnMinimumSeverity)))
                {
                    additionalIssues.Add(nonBlocking);
                    if (nonBlocking.Severity == RuleSeverity.Warning)
                    {
                        warnings.Add(ValidationMessage.For(
                            ValidationSources.Rule,
                            $"{fixtureId} warning [{nonBlocking.RuleId ?? "rule"}]: {nonBlocking.Message}",
                            identifier: fixtureId,
                            path: nonBlocking.PropertyPath,
                            ruleId: nonBlocking.RuleId,
                            engineType: logicValidator.GetType(),
                            metadata: new Dictionary<string, object?> { ["capturedInIssues"] = true }));
                    }
                }
            }
        }

        if (payloadParsed && identifierAlignment.IsEnabled && candidateSchema is not null)
        {
            EvaluateValidFixtureIdentifier(
                payload,
                fixtureId,
                identifierAlignment,
                effectiveIdentifierType,
                identifierTrace,
                fixtureErrors);
        }
    }

    private void EvaluateValidFixtureIdentifier(
        string payload,
        string fixtureId,
        IdentifierSchemaAlignmentResult identifierAlignment,
        string effectiveIdentifierType,
        EngineRunState identifierTrace,
        List<ValidationMessage> fixtureErrors)
    {
        var idStart = Stopwatch.GetTimestamp();
        identifierTrace.ItemsEvaluated++;
        var idInspect = SubmissionIdentifier.InspectPayload(
            payload,
            identifierAlignment.ResolvedPaths,
            effectiveIdentifierType);
        identifierTrace.ElapsedTicks += Stopwatch.GetTimestamp() - idStart;

        if (!idInspect.IsSuccess)
        {
            return;
        }

        if (idInspect.Value.Status == IdentifierExtractionStatus.Invalid)
        {
            identifierTrace.ViolationCount++;
            identifierTrace.FirstIssue ??= $"{fixtureId} failed identifier extraction: {idInspect.Value.ErrorMessage}";

            fixtureErrors.Add(ValidationMessage.For(
                ValidationSources.Identifier,
                $"{fixtureId} failed identifier extraction: {idInspect.Value.ErrorMessage}",
                identifier: fixtureId,
                path: idInspect.Value.MatchedPath,
                engineType: typeof(SubmissionIdentifier),
                metadata: new Dictionary<string, object?> { ["identifierType"] = effectiveIdentifierType }));
        }
        else if (idInspect.Value.Status == IdentifierExtractionStatus.NotPresent && identifierAlignment.IsRequiredInSchema)
        {
            var expectedPath = idInspect.Value.MatchedPath
                ?? identifierAlignment.ResolvedPaths.FirstOrDefault()
                ?? _validationOptions.DefaultIdentifierPointerFallback;
            var msg = $"{fixtureId} is missing required '{effectiveIdentifierType}' identifier at '{expectedPath}'.";
            identifierTrace.ViolationCount++;
            identifierTrace.FirstIssue ??= msg;

            fixtureErrors.Add(ValidationMessage.For(
                ValidationSources.Identifier,
                msg,
                identifier: fixtureId,
                path: expectedPath,
                engineType: typeof(SubmissionIdentifier),
                metadata: new Dictionary<string, object?>
                {
                    ["identifierType"] = effectiveIdentifierType,
                    ["required"] = true
                }));
        }
    }

    private async Task<InvalidFixtureOutcome> EvaluateInvalidFixtureAsync(
        int index,
        string rawFixture,
        JsonSchema? candidateSchema,
        IReadOnlyList<BusinessRule>? candidateRules,
        IdentifierSchemaAlignmentResult identifierAlignment,
        string effectiveIdentifierType,
        SchemaValidationPolicy effectivePolicy,
        EvaluationOptions evalOptions,
        IReadOnlyDictionary<string, JsonNode?>? ambientFixtures,
        EngineRunState schemaTrace,
        EngineRunState identifierTrace,
        IEnumerable<EngineRunState> allRuleStates,
        Func<string, EngineRunState> getOrCreateRuleTrace,
        List<ValidationMessage> fixtureErrors,
        List<ValidationMessage> warnings,
        CancellationToken cancellationToken)
    {
        var spec = InvalidFixtureParser.Parse(rawFixture);
        var payload = spec.PayloadJson;
        var fixtureId = $"SampleInvalidFixtures:{index}";
        var schemaFailed = false;
        var rulesFailed = false;
        var identifierFailed = false;
        var triggeredErrors = new List<ValidationMessage>();

        if (candidateSchema is not null)
        {
            var schemaFixtureStart = Stopwatch.GetTimestamp();
            schemaTrace.ItemsEvaluated++;
            try
            {
                using var invalidDoc = JsonDocument.Parse(payload);
                var eval = candidateSchema.Evaluate(invalidDoc.RootElement, evalOptions);
                if (!eval.IsValid)
                {
                    schemaFailed = true;
                    schemaTrace.InvalidTriggers++;
                    CollectSchemaErrors(eval, fixtureId, triggeredErrors);
                }
            }
            catch (Exception ex)
            {
                if (candidateRules is null)
                {
                    fixtureErrors.Add(ValidationMessage.For(
                        ValidationSources.Json,
                        $"{fixtureId} could not be parsed: {ex.Message}",
                        identifier: fixtureId,
                        engineType: typeof(JsonSchema)));
                }
                schemaFailed = true;
                schemaTrace.InvalidTriggers++;
                triggeredErrors.Add(ValidationMessage.For(
                    ValidationSources.Json,
                    $"Could not parse payload: {ex.Message}",
                    identifier: fixtureId,
                    engineType: typeof(JsonNode)));
            }
            finally
            {
                schemaTrace.ElapsedTicks += Stopwatch.GetTimestamp() - schemaFixtureStart;
            }
        }

        if (candidateRules is { Count: > 0 })
        {
            var rulesStart = Stopwatch.GetTimestamp();
            var logicResult = await logicValidator.ValidateAsync(
                payload,
                candidateRules,
                ambientFixtures,
                (double?)effectivePolicy.NumericToleranceOverride,
                cancellationToken);
            var rulesTicks = Stopwatch.GetTimestamp() - rulesStart;

            if (logicResult.IsFailure)
            {
                rulesFailed = true;
                foreach (var state in allRuleStates.Where(s => s.RuleCount > 0))
                {
                    state.InvalidTriggers++;
                    state.ElapsedTicks += rulesTicks;
                }

                triggeredErrors.Add(ValidationMessage.For(
                    ValidationSources.Rule,
                    logicResult.Error.Description ?? logicResult.Error.Code,
                    identifier: fixtureId,
                    engineType: logicValidator.GetType(),
                    metadata: new Dictionary<string, object?> { ["errorCode"] = logicResult.Error.Code }));
            }
            else
            {
                MergeRuleEngineTracesForInvalidFixture(
                    logicResult.Value,
                    candidateRules,
                    rulesTicks,
                    getOrCreateRuleTrace);

                var actionableIssues = (logicResult.Value.Errors ?? [])
                    .Where(e =>
                    {
                        if (e.Severity != RuleSeverity.Info) return true;
                        var matchedRule = candidateRules.FirstOrDefault(r => string.Equals(r.Id, e.RuleId, StringComparison.OrdinalIgnoreCase));
                        return matchedRule?.EffectiveAssertion is not null;
                    })
                    .ToList();

                if (actionableIssues.Count > 0)
                {
                    rulesFailed = true;
                    foreach (var err in actionableIssues)
                    {
                        var path = string.IsNullOrWhiteSpace(err.PropertyPath) ? "/" : err.PropertyPath;
                        var ruleId = string.IsNullOrWhiteSpace(err.RuleId) ? null : err.RuleId;
                        triggeredErrors.Add(ValidationMessage.For(
                            ValidationSources.Rule,
                            err.Message,
                            identifier: fixtureId,
                            path: path,
                            ruleId: ruleId,
                            engineType: logicValidator.GetType()));
                    }
                }
            }
        }

        if (identifierAlignment.IsEnabled && candidateSchema is not null)
        {
            var idStart = Stopwatch.GetTimestamp();
            identifierTrace.ItemsEvaluated++;
            var idInspect = SubmissionIdentifier.InspectPayload(
                payload,
                identifierAlignment.ResolvedPaths,
                effectiveIdentifierType);
            identifierTrace.ElapsedTicks += Stopwatch.GetTimestamp() - idStart;

            if (idInspect.IsSuccess)
            {
                if (idInspect.Value.Status == IdentifierExtractionStatus.Invalid)
                {
                    identifierFailed = true;
                    identifierTrace.InvalidTriggers++;
                    triggeredErrors.Add(ValidationMessage.For(
                        ValidationSources.Identifier,
                        idInspect.Value.ErrorMessage ?? "Invalid identifier.",
                        identifier: fixtureId,
                        path: idInspect.Value.MatchedPath,
                        engineType: typeof(SubmissionIdentifier),
                        metadata: new Dictionary<string, object?> { ["identifierType"] = effectiveIdentifierType }));
                }
                else if (idInspect.Value.Status == IdentifierExtractionStatus.NotPresent && identifierAlignment.IsRequiredInSchema)
                {
                    identifierFailed = true;
                    identifierTrace.InvalidTriggers++;
                    var expectedPath = idInspect.Value.MatchedPath
                        ?? identifierAlignment.ResolvedPaths.FirstOrDefault()
                        ?? _validationOptions.DefaultIdentifierPointerFallback;
                    triggeredErrors.Add(ValidationMessage.For(
                        ValidationSources.Identifier,
                        $"Missing required '{effectiveIdentifierType}' identifier at '{expectedPath}'.",
                        identifier: fixtureId,
                        path: expectedPath,
                        engineType: typeof(SubmissionIdentifier),
                        metadata: new Dictionary<string, object?>
                        {
                            ["identifierType"] = effectiveIdentifierType,
                            ["required"] = true
                        }));
                }
            }
        }

        var distinctTriggered = triggeredErrors.Distinct().ToList();
        var anyValidatorConfigured = candidateSchema is not null || candidateRules is not null;
        var failedValidation = schemaFailed || rulesFailed || identifierFailed;
        var failedAsExpected = !anyValidatorConfigured || failedValidation;
        var unmatchedExpected = new List<string>();
        string? fixtureWarning = null;

        if (anyValidatorConfigured && !failedValidation)
        {
            fixtureErrors.Add(ValidationMessage.For(
                ValidationSources.Verification,
                $"{fixtureId} was expected to fail, but passed the schema.",
                identifier: fixtureId,
                engineType: typeof(SchemaVerificationService)));
        }
        else if (spec.ExpectedErrors.Count > 0)
        {
            foreach (var expectedPattern in spec.ExpectedErrors)
            {
                if (!distinctTriggered.Any(actual => actual.MatchesPattern(expectedPattern)))
                {
                    unmatchedExpected.Add(expectedPattern);
                }
            }

            if (unmatchedExpected.Count > 0)
            {
                failedAsExpected = false;
                var actualSummary = distinctTriggered.Count > 0
                    ? string.Join("; ", distinctTriggered.Select(e => e.FormatDisplay()))
                    : "none";
                fixtureErrors.Add(ValidationMessage.For(
                    ValidationSources.Verification,
                    $"{fixtureId} did not trigger expected error(s) [{string.Join(", ", unmatchedExpected)}]. Actual triggered errors: {actualSummary}.",
                    identifier: fixtureId,
                    engineType: typeof(SchemaVerificationService),
                    metadata: new Dictionary<string, object?> { ["unmatchedExpectedErrors"] = unmatchedExpected.ToArray() }));
            }
        }
        else if (anyValidatorConfigured && failedValidation)
        {
            var actualSummary = distinctTriggered.Count > 0
                ? string.Join("; ", distinctTriggered.Select(e => e.FormatDisplay()))
                : "validation rejected payload";
            fixtureWarning =
                $"Failed validation, but unknown if expected because no expectedErrors assertions were configured. Triggered: {actualSummary}.";
            warnings.Add(ValidationMessage.For(
                ValidationSources.Verification,
                $"{fixtureId} failed validation, but unknown if expected (no expectedErrors configured). Triggered error(s): {actualSummary}.",
                identifier: fixtureId,
                engineType: typeof(SchemaVerificationService),
                metadata: new Dictionary<string, object?> { ["triggeredCount"] = distinctTriggered.Count }));
        }

        return new InvalidFixtureOutcome(
            Index: index,
            FailedAsExpected: failedAsExpected,
            TriggeredErrors: distinctTriggered,
            ExpectedErrors: spec.ExpectedErrors,
            UnmatchedExpectedErrors: unmatchedExpected,
            Description: spec.Description,
            Warning: fixtureWarning);
    }

    private static void CollectSchemaErrors(
        EvaluationResults eval,
        string fixtureId,
        List<ValidationMessage> target,
        bool addFallbackIfEmpty = true,
        bool prefixMessageWithIdentifier = false)
    {
        var added = 0;

        if (eval.Errors is { Count: > 0 })
        {
            var loc = string.IsNullOrWhiteSpace(eval.InstanceLocation.ToString()) ? "/" : eval.InstanceLocation.ToString();
            foreach (var (keyword, msg) in eval.Errors)
            {
                target.Add(ValidationMessage.For(
                    ValidationSources.Schema,
                    prefixMessageWithIdentifier ? $"{fixtureId} {msg}" : msg,
                    identifier: fixtureId,
                    path: loc,
                    engineType: typeof(JsonSchema),
                    metadata: new Dictionary<string, object?> { ["keyword"] = keyword }));
                added++;
            }
        }

        if (eval.Details is { Count: > 0 })
        {
            foreach (var detail in eval.Details.Where(d => d.Errors is { Count: > 0 }))
            {
                var loc = string.IsNullOrWhiteSpace(detail.InstanceLocation.ToString()) ? "/" : detail.InstanceLocation.ToString();
                foreach (var (keyword, msg) in detail.Errors!)
                {
                    target.Add(ValidationMessage.For(
                        ValidationSources.Schema,
                        prefixMessageWithIdentifier ? $"{fixtureId} {msg}" : msg,
                        identifier: fixtureId,
                        path: loc,
                        engineType: typeof(JsonSchema),
                        metadata: new Dictionary<string, object?> { ["keyword"] = keyword }));
                    added++;
                }
            }
        }

        if (addFallbackIfEmpty && added == 0)
        {
            target.Add(ValidationMessage.For(
                ValidationSources.Schema,
                "Payload rejected by JSON Schema.",
                identifier: fixtureId,
                engineType: typeof(JsonSchema)));
        }
    }
}
