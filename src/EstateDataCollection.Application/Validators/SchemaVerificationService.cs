using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using Json.Schema;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.Validators;

public sealed class SchemaVerificationService(
    IBusinessLogicValidator logicValidator,
    IOdcsContractParser? odcsParser = null,
    IOptions<ValidationOptions>? validationOptions = null,
    SchemaVerificationServiceInstrumentation? instrumentation = null,
    ISchemaValidator? schemaValidator = null
) : ISchemaVerificationService
{
    private static readonly JsonSerializerOptions RuleJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ValidationOptions _validationOptions = validationOptions?.Value ?? new ValidationOptions();
    private readonly SchemaVerificationServiceInstrumentation _instrumentation =
        instrumentation ?? new SchemaVerificationServiceInstrumentation(NullLogger<SchemaVerificationService>.Instance);

    private readonly ValidationEngineDescriptor _schemaEngineDescriptor =
        schemaValidator?.Descriptor ?? new ValidationEngineDescriptor(
            EngineId: "json-schema",
            DisplayName: "JSON Schema Validator",
            Kind: ValidationEngineKinds.StructuralSchema,
            Description: "Structural JSON Schema validator.");

    public async Task<Result<SchemaVerificationResult>> VerifyCandidateSchemaAsync(
        string? rawSchemaJson,
        string? rawRulesJson,
        IReadOnlyList<string> sampleValidPayloads,
        IReadOnlyList<string> sampleInvalidPayloads,
        SchemaIdentifierConfig? identifierConfig = null,
        CancellationToken cancellationToken = default,
        SchemaValidationPolicy? validationPolicy = null)
    {
        var syntaxErrors = new List<ValidationMessage>();
        var fixtureErrors = new List<ValidationMessage>();
        var warnings = new List<ValidationMessage>();

        var registeredRuleEngines = logicValidator.GetRegisteredEngines() ?? [];
        var schemaTrace = new EngineRunState(_schemaEngineDescriptor);
        var ruleTraces = new Dictionary<string, EngineRunState>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in registeredRuleEngines)
        {
            ruleTraces[descriptor.EngineId] = new EngineRunState(descriptor);
        }

        var identifierTrace = new EngineRunState(PayloadIdentifierExtractor.Descriptor);

        EngineRunState GetOrCreateRuleTrace(string engineId)
        {
            if (!ruleTraces.TryGetValue(engineId, out var state))
            {
                var descriptor = new ValidationEngineDescriptor(
                    EngineId: engineId,
                    DisplayName: engineId,
                    Kind: ValidationEngineKinds.QualityRule,
                    Description: $"Quality rule engine '{engineId}'.");
                state = new EngineRunState(descriptor);
                ruleTraces[engineId] = state;
            }

            return state;
        }

        JsonSchema? candidateSchema = null;
        IReadOnlyList<BusinessRule>? candidateRules = null;
        SchemaValidationPolicy? odcsValidationPolicy = null;

        if (string.IsNullOrWhiteSpace(rawSchemaJson) && string.IsNullOrWhiteSpace(rawRulesJson))
        {
            return new SchemaVerificationResult(
                [ValidationMessage.For(ValidationSources.Verification, "Either Schema JSON/YAML or Rules JSON must be provided.", identifier: "schema", engineType: typeof(SchemaVerificationService))],
                [],
                EngineTraces: BuildEngineTraces(
                    schemaTrace,
                    ruleTraces.Values,
                    identifierTrace,
                    hasSchema: false,
                    hasRules: false,
                    identifierEnabled: false,
                    validFixtureCount: 0,
                    invalidFixtureCount: 0,
                    identifierLabel: null));
        }

        // Route Detection: If rawSchemaJson is an ODCS YAML contract, parse it into Schema JSON + Rules JSON
        if (!string.IsNullOrWhiteSpace(rawSchemaJson) && odcsParser is not null && odcsParser.IsOdcsYaml(rawSchemaJson))
        {
            var odcsResult = odcsParser.Parse(rawSchemaJson);
            if (odcsResult.IsFailure)
            {
                var msg = odcsResult.Error.Description ?? "Failed to parse ODCS YAML contract.";
                _instrumentation.CandidateVerificationCompleted(false, 1, 0);
                return new SchemaVerificationResult(
                    [ValidationMessage.For(ValidationSources.Odcs, msg, identifier: "odcs", engineType: odcsParser.GetType())],
                    [],
                    EngineTraces: BuildEngineTraces(
                        schemaTrace,
                        ruleTraces.Values,
                        identifierTrace,
                        hasSchema: false,
                        hasRules: false,
                        identifierEnabled: false,
                        validFixtureCount: 0,
                        invalidFixtureCount: 0,
                        identifierLabel: null));
            }

            rawSchemaJson = odcsResult.Value.GeneratedSchemaJson;
            odcsValidationPolicy = odcsResult.Value.ValidationPolicy;
            if (string.IsNullOrWhiteSpace(rawRulesJson) && !string.IsNullOrWhiteSpace(odcsResult.Value.GeneratedRulesJson))
            {
                rawRulesJson = odcsResult.Value.GeneratedRulesJson;
            }
        }

        var hasSchemaInput = !string.IsNullOrWhiteSpace(rawSchemaJson);
        var hasRulesInput = !string.IsNullOrWhiteSpace(rawRulesJson);

        // Phase One A: Syntax & Meta-Schema Evaluation (Draft 2020-12 & Draft-07)
        if (hasSchemaInput)
        {
            var schemaPhaseStart = Stopwatch.GetTimestamp();
            schemaTrace.ItemsEvaluated++;
            try
            {
                var node = JsonNode.Parse(rawSchemaJson!);

                if (node is null)
                {
                    schemaTrace.ViolationCount++;
                    schemaTrace.FirstIssue ??= "Schema JSON was empty or null.";
                    syntaxErrors.Add(ValidationMessage.For(
                        ValidationSources.Schema,
                        "Schema JSON was empty or null.",
                        identifier: "schema",
                        engineType: typeof(JsonSchema)));
                }
                else
                {
                    var meta = ResolveMetaSchema(node, syntaxErrors);
                    if (meta is not null)
                    {
                        using var schemaDoc = JsonDocument.Parse(rawSchemaJson!);
                        var metaResult = meta.Evaluate(schemaDoc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

                        if (!metaResult.IsValid)
                        {
                            var rootErrors = metaResult.Errors?
                                .Select(err =>
                                {
                                    var loc = metaResult.InstanceLocation.ToString();
                                    var id = string.IsNullOrWhiteSpace(loc) ? "schema" : loc;
                                    var path = string.IsNullOrWhiteSpace(loc) ? null : loc;
                                    return ValidationMessage.For(
                                        ValidationSources.Schema,
                                        err.Value,
                                        identifier: id,
                                        path: path,
                                        engineType: typeof(JsonSchema),
                                        metadata: new Dictionary<string, object?> { ["keyword"] = err.Key });
                                }) ?? [];

                            var detailErrors = (metaResult.Details ?? [])
                                .Where(x => x.Errors is { Count: > 0 })
                                .SelectMany(x =>
                                {
                                    var loc = x.InstanceLocation.ToString();
                                    var id = string.IsNullOrWhiteSpace(loc) ? "schema" : loc;
                                    var path = string.IsNullOrWhiteSpace(loc) ? null : loc;
                                    return x.Errors!.Select(err => ValidationMessage.For(
                                        ValidationSources.Schema,
                                        err.Value,
                                        identifier: id,
                                        path: path,
                                        engineType: typeof(JsonSchema),
                                        metadata: new Dictionary<string, object?> { ["keyword"] = err.Key }));
                                });

                            var errors = rootErrors.Concat(detailErrors).Distinct().ToList();

                            schemaTrace.ViolationCount += errors.Count;
                            schemaTrace.FirstIssue ??= errors.FirstOrDefault()?.Message;
                            syntaxErrors.AddRange(errors);
                        }
                    }
                    else
                    {
                        schemaTrace.ViolationCount++;
                        schemaTrace.FirstIssue ??= syntaxErrors.LastOrDefault()?.Message;
                    }

                    candidateSchema = JsonSchema.FromText(rawSchemaJson!);
                }
            }
            catch (Exception ex)
            {
                schemaTrace.ElapsedTicks += Stopwatch.GetTimestamp() - schemaPhaseStart;
                schemaTrace.HasFailure = true;
                schemaTrace.ViolationCount++;
                schemaTrace.FirstIssue ??= ex.Message;

                return new SchemaVerificationResult(
                    [
                        ValidationMessage.For(ValidationSources.Schema, "Malformed JSON Schema syntax", identifier: "schema", engineType: typeof(JsonSchema)),
                        ValidationMessage.For(ValidationSources.Schema, ex.Message, identifier: "schema", engineType: typeof(JsonSchema))
                    ],
                    [],
                    EngineTraces: BuildEngineTraces(
                        schemaTrace,
                        ruleTraces.Values,
                        identifierTrace,
                        hasSchema: true,
                        hasRules: hasRulesInput,
                        identifierEnabled: false,
                        validFixtureCount: 0,
                        invalidFixtureCount: 0,
                        identifierLabel: null));
            }

            schemaTrace.ElapsedTicks += Stopwatch.GetTimestamp() - schemaPhaseStart;
        }

        // Phase One B: Syntax & Engine Validation for Quality Rules
        if (hasRulesInput)
        {
            var rulesSyntaxStart = Stopwatch.GetTimestamp();
            var logicSyntaxResult = logicValidator.ValidateRulesSyntax(rawRulesJson!, out candidateRules);
            var rulesSyntaxTicks = Stopwatch.GetTimestamp() - rulesSyntaxStart;

            if (candidateRules is { Count: > 0 })
            {
                foreach (var ruleGroup in candidateRules.GroupBy(
                             r => r.ResolveEngine(_validationOptions.DefaultEngine),
                             StringComparer.OrdinalIgnoreCase))
                {
                    var engineState = GetOrCreateRuleTrace(ruleGroup.Key);
                    engineState.RuleCount = ruleGroup.Count();
                    engineState.ItemsEvaluated += engineState.RuleCount;
                    engineState.ElapsedTicks += rulesSyntaxTicks;
                }
            }
            else if (logicSyntaxResult.IsSuccess && logicSyntaxResult.Value.Count > 0)
            {
                RecordRuleSyntaxErrors(
                    rawRulesJson!,
                    logicSyntaxResult.Value,
                    rulesSyntaxTicks,
                    GetOrCreateRuleTrace,
                    ruleTraces.Values);
            }

            if (logicSyntaxResult.IsSuccess && logicSyntaxResult.Value.Count > 0)
            {
                syntaxErrors.AddRange(logicSyntaxResult.Value);
            }
        }

        // Phase One C: Identifier Config Alignment Against Schema AST & Derived Requiredness
        var idAlignStart = Stopwatch.GetTimestamp();
        var identifierAlignment = IdentifierSchemaValidator.ValidateAndAlign(
            candidateSchema is not null ? rawSchemaJson : null,
            identifierConfig,
            _validationOptions.DefaultIdentifierJsonPaths,
            _validationOptions.RequireIdentifierOnFinalise);
        identifierTrace.ElapsedTicks += Stopwatch.GetTimestamp() - idAlignStart;

        if (identifierAlignment.IsEnabled && candidateSchema is not null)
        {
            identifierTrace.ItemsEvaluated++;
        }

        if (!identifierAlignment.IsValid)
        {
            identifierTrace.ViolationCount += identifierAlignment.Errors.Count;
            identifierTrace.FirstIssue ??= identifierAlignment.Errors.FirstOrDefault()?.Message;
            syntaxErrors.AddRange(identifierAlignment.Errors);
        }

        var effectiveIdentifierType = identifierAlignment.EffectiveConfig?.Type ?? _validationOptions.DefaultIdentifierType;
        var effectiveIdentifierLabel = FormatIdentifierLabel(identifierAlignment, effectiveIdentifierType);

        if (syntaxErrors.Count > 0)
        {
            _instrumentation.CandidateVerificationCompleted(false, syntaxErrors.Count, 0);
            return new SchemaVerificationResult(
                syntaxErrors,
                [],
                ResolvedIdentifierConfig: identifierAlignment.EffectiveConfig,
                EngineTraces: BuildEngineTraces(
                    schemaTrace,
                    ruleTraces.Values,
                    identifierTrace,
                    hasSchema: candidateSchema is not null || hasSchemaInput,
                    hasRules: hasRulesInput,
                    identifierEnabled: identifierAlignment.IsEnabled && candidateSchema is not null,
                    validFixtureCount: 0,
                    invalidFixtureCount: 0,
                    identifierLabel: effectiveIdentifierLabel));
        }

        // Phase Two: In-Flight Golden Fixture Dry-Runs (against Schema, Quality Rules, and Identifier Extraction)
        var evalOptions = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = _validationOptions.RequireFormatValidation
        };

        // Ensure valid fixtures pass schema, quality rules, and identifier extraction
        for (var i = 0; i < sampleValidPayloads.Count; i++)
        {
            var payload = sampleValidPayloads[i];
            var fixtureId = $"SampleValidFixtures:{i}";
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

                        fixtureErrors.AddRange((eval.Details?.Where(x => x.Errors is { Count: > 0 }) ?? [])
                            .Select(detail => ValidationMessage.For(
                                ValidationSources.Schema,
                                $"{fixtureId} {string.Join(", ", detail.Errors!.Values)}",
                                identifier: fixtureId,
                                path: string.IsNullOrWhiteSpace(detail.InstanceLocation.ToString()) ? "/" : detail.InstanceLocation.ToString(),
                                engineType: typeof(JsonSchema))));

                        fixtureErrors.AddRange((eval.Errors ?? new Dictionary<string, string>())
                            .Select(err => ValidationMessage.For(
                                ValidationSources.Schema,
                                $"{fixtureId} {err.Value}",
                                identifier: fixtureId,
                                path: string.IsNullOrWhiteSpace(eval.InstanceLocation.ToString()) ? "/" : eval.InstanceLocation.ToString(),
                                engineType: typeof(JsonSchema),
                                metadata: new Dictionary<string, object?> { ["keyword"] = err.Key })));
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
                var logicResult = await logicValidator.ValidateAsync(payload, candidateRules, cancellationToken);
                var rulesTicks = Stopwatch.GetTimestamp() - rulesStart;

                if (logicResult.IsFailure)
                {
                    foreach (var state in ruleTraces.Values.Where(s => s.RuleCount > 0))
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
                        GetOrCreateRuleTrace);

                    var effectivePolicy = (validationPolicy ?? odcsValidationPolicy ?? SchemaValidationPolicy.Default).Normalise();
                    var allRuleIssues = logicResult.Value.Errors ?? [];
                    var blockingRuleIssues = ValidationIssuesHelper.GetBlockingIssues(allRuleIssues, effectivePolicy.FailOnMinimumSeverity);
                    if (blockingRuleIssues.Count > 0)
                    {
                        var ruleErrors = string.Join("; ", blockingRuleIssues.Select(e => $"{e.PropertyPath}: {e.Message}"));
                        var ruleIds = blockingRuleIssues.Select(e => e.RuleId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
                        fixtureErrors.Add(ValidationMessage.For(
                            ValidationSources.Rule,
                            $"{fixtureId} violated quality rules: {ruleErrors}",
                            identifier: fixtureId,
                            ruleId: ruleIds.Length == 1 ? ruleIds[0] : null,
                            engineType: logicValidator.GetType(),
                            metadata: ruleIds.Length > 0
                                ? new Dictionary<string, object?> { ["ruleIds"] = ruleIds }
                                : null));
                    }

                    foreach (var nonBlocking in allRuleIssues.Where(e => !e.Severity.IsAtOrAbove(effectivePolicy.FailOnMinimumSeverity) && e.Severity == RuleSeverity.Warning))
                    {
                        warnings.Add(ValidationMessage.For(
                            ValidationSources.Rule,
                            $"{fixtureId} warning [{nonBlocking.RuleId ?? "rule"}]: {nonBlocking.Message}",
                            identifier: fixtureId,
                            path: nonBlocking.PropertyPath,
                            ruleId: nonBlocking.RuleId,
                            engineType: logicValidator.GetType()));
                    }
                }
            }

            if (payloadParsed && identifierAlignment.IsEnabled && candidateSchema is not null)
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
            }
        }

        // Ensure invalid fixtures fail schema, quality rules, or identifier extraction
        var invalidFixtureOutcomes = new List<InvalidFixtureOutcome>(sampleInvalidPayloads.Count);

        for (var i = 0; i < sampleInvalidPayloads.Count; i++)
        {
            var spec = InvalidFixtureParser.Parse(sampleInvalidPayloads[i]);
            var payload = spec.PayloadJson;
            var fixtureId = $"SampleInvalidFixtures:{i}";
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
                var logicResult = await logicValidator.ValidateAsync(payload, candidateRules, cancellationToken);
                var rulesTicks = Stopwatch.GetTimestamp() - rulesStart;

                if (logicResult.IsFailure)
                {
                    rulesFailed = true;
                    foreach (var state in ruleTraces.Values.Where(s => s.RuleCount > 0))
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
                        GetOrCreateRuleTrace);

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

            invalidFixtureOutcomes.Add(new InvalidFixtureOutcome(
                Index: i,
                FailedAsExpected: failedAsExpected,
                TriggeredErrors: distinctTriggered,
                ExpectedErrors: spec.ExpectedErrors,
                UnmatchedExpectedErrors: unmatchedExpected,
                Description: spec.Description,
                Warning: fixtureWarning));
        }

        var isSuccess = syntaxErrors.Count == 0 && fixtureErrors.Count == 0;
        _instrumentation.CandidateVerificationCompleted(isSuccess, syntaxErrors.Count, fixtureErrors.Count);

        var traces = BuildEngineTraces(
            schemaTrace,
            ruleTraces.Values,
            identifierTrace,
            hasSchema: candidateSchema is not null,
            hasRules: hasRulesInput && candidateRules is { Count: > 0 },
            identifierEnabled: identifierAlignment.IsEnabled && candidateSchema is not null,
            validFixtureCount: sampleValidPayloads.Count,
            invalidFixtureCount: sampleInvalidPayloads.Count,
            identifierLabel: effectiveIdentifierLabel);

        return new SchemaVerificationResult(
            syntaxErrors,
            fixtureErrors,
            ResolvedIdentifierConfig: identifierAlignment.EffectiveConfig,
            InvalidFixtureOutcomes: invalidFixtureOutcomes,
            Warnings: warnings,
            EngineTraces: traces);
    }

    private void RecordRuleSyntaxErrors(
        string rawRulesJson,
        IReadOnlyList<ValidationMessage> syntaxErrors,
        long elapsedTicks,
        Func<string, EngineRunState> getOrCreateRuleTrace,
        IEnumerable<EngineRunState> registeredStates)
    {
        List<BusinessRule>? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<List<BusinessRule>>(rawRulesJson, RuleJsonOptions);
        }
        catch
        {
            // Malformed JSON array — handled below by applying syntax error to registered rule engines.
        }

        if (parsed is { Count: > 0 })
        {
            var rulesByEngine = parsed
                .GroupBy(r => r.ResolveEngine(_validationOptions.DefaultEngine), StringComparer.OrdinalIgnoreCase);

            foreach (var group in rulesByEngine)
            {
                var state = getOrCreateRuleTrace(group.Key);
                state.RuleCount = group.Count();
                state.ItemsEvaluated += group.Count();
                state.ElapsedTicks += elapsedTicks;

                var ruleIds = group.Select(r => r.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var matchingErrors = syntaxErrors
                    .Where(e => !string.IsNullOrWhiteSpace(e.RuleId) && ruleIds.Contains(e.RuleId))
                    .ToList();

                if (matchingErrors.Count > 0)
                {
                    state.ViolationCount += matchingErrors.Count;
                    state.FirstIssue ??= matchingErrors[0].Message;
                }
            }

            if (syntaxErrors.All(e => string.IsNullOrWhiteSpace(e.RuleId)))
            {
                foreach (var state in registeredStates.Where(s => s.RuleCount > 0))
                {
                    state.ViolationCount += syntaxErrors.Count;
                    state.FirstIssue ??= syntaxErrors[0].Message;
                }
            }

            return;
        }

        var firstMessage = syntaxErrors[0].Message;
        foreach (var state in registeredStates)
        {
            state.ItemsEvaluated++;
            state.ElapsedTicks += elapsedTicks;
            state.ViolationCount += syntaxErrors.Count;
            state.FirstIssue ??= firstMessage;
        }
    }

    private void MergeRuleEngineTracesForValidFixture(
        BusinessRuleValidationResult validationResult,
        IReadOnlyList<BusinessRule> candidateRules,
        long fallbackTicks,
        Func<string, EngineRunState> getOrCreateRuleTrace)
    {
        if (validationResult.EngineTraces is { Count: > 0 } traces)
        {
            foreach (var trace in traces)
            {
                var state = getOrCreateRuleTrace(trace.Descriptor.EngineId);
                state.ItemsEvaluated += trace.ItemsEvaluated;
                state.ViolationCount += trace.ViolationCount;
                state.ExplicitDurationMs += trace.DurationMs;

                if (string.Equals(trace.Outcome, nameof(SubmissionProcessOutcome.Failed), StringComparison.Ordinal))
                {
                    state.HasFailure = true;
                    state.FirstIssue ??= trace.Summary;
                }
                else if (trace.ViolationCount > 0)
                {
                    state.FirstIssue ??= trace.Summary;
                }
            }

            return;
        }

        foreach (var group in candidateRules.GroupBy(
                     r => r.ResolveEngine(_validationOptions.DefaultEngine),
                     StringComparer.OrdinalIgnoreCase))
        {
            var state = getOrCreateRuleTrace(group.Key);
            state.ItemsEvaluated += group.Count();
            state.ElapsedTicks += fallbackTicks;
        }

        if (!validationResult.IsValid && validationResult.Errors is { Count: > 0 })
        {
            foreach (var state in candidateRules
                         .Select(r => r.ResolveEngine(_validationOptions.DefaultEngine))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Select(getOrCreateRuleTrace))
            {
                state.ViolationCount += validationResult.Errors.Count;
                state.FirstIssue ??= validationResult.Errors[0].Message;
            }
        }
    }

    private void MergeRuleEngineTracesForInvalidFixture(
        BusinessRuleValidationResult validationResult,
        IReadOnlyList<BusinessRule> candidateRules,
        long fallbackTicks,
        Func<string, EngineRunState> getOrCreateRuleTrace)
    {
        if (validationResult.EngineTraces is { Count: > 0 } traces)
        {
            foreach (var trace in traces)
            {
                var state = getOrCreateRuleTrace(trace.Descriptor.EngineId);
                state.ItemsEvaluated += trace.ItemsEvaluated;
                state.ExplicitDurationMs += trace.DurationMs;
                if (trace.ViolationCount > 0)
                {
                    state.InvalidTriggers++;
                }
            }

            return;
        }

        foreach (var group in candidateRules.GroupBy(
                     r => r.ResolveEngine(_validationOptions.DefaultEngine),
                     StringComparer.OrdinalIgnoreCase))
        {
            var state = getOrCreateRuleTrace(group.Key);
            state.ItemsEvaluated += group.Count();
            state.ElapsedTicks += fallbackTicks;
            if (!validationResult.IsValid)
            {
                state.InvalidTriggers++;
            }
        }
    }

    private static string? FormatIdentifierLabel(
        IdentifierSchemaAlignmentResult alignment,
        string effectiveType)
    {
        if (!alignment.IsEnabled)
        {
            return null;
        }

        var ptr = alignment.EffectiveConfig?.JsonPointer
                  ?? alignment.ResolvedPaths.FirstOrDefault()
                  ?? string.Empty;
        return $"Type '{effectiveType}' at '{ptr}' (required: {alignment.IsRequiredInSchema})";
    }

    private static IReadOnlyList<ValidationEngineTrace> BuildEngineTraces(
        EngineRunState schemaState,
        IEnumerable<EngineRunState> ruleStates,
        EngineRunState identifierState,
        bool hasSchema,
        bool hasRules,
        bool identifierEnabled,
        int validFixtureCount,
        int invalidFixtureCount,
        string? identifierLabel)
    {
        var traces = new List<ValidationEngineTrace>();

        // 1. Structural JSON Schema Engine
        if (!hasSchema && !schemaState.HasFailure && schemaState.ViolationCount == 0)
        {
            traces.Add(new ValidationEngineTrace(
                schemaState.Descriptor,
                nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: 0,
                ViolationCount: 0,
                DurationMs: 0,
                Summary: "No JSON Schema provided"));
        }
        else
        {
            var schemaOutcome = schemaState.HasFailure
                ? nameof(SubmissionProcessOutcome.Failed)
                : schemaState.ViolationCount > 0
                    ? nameof(SubmissionProcessOutcome.ValidationErrorsFound)
                    : nameof(SubmissionProcessOutcome.Succeeded);

            var schemaSummary = schemaState.HasFailure
                ? schemaState.FirstIssue ?? "Malformed JSON Schema"
                : schemaState.ViolationCount > 0
                    ? schemaState.FirstIssue ?? $"{schemaState.ViolationCount} schema error(s)"
                    : BuildFixtureSummary("Schema valid", validFixtureCount, invalidFixtureCount, schemaState.InvalidTriggers);

            traces.Add(new ValidationEngineTrace(
                schemaState.Descriptor,
                schemaOutcome,
                ItemsEvaluated: schemaState.ItemsEvaluated,
                ViolationCount: schemaState.ViolationCount,
                DurationMs: schemaState.TotalDurationMs,
                Summary: schemaSummary));
        }

        // 2. Registered & Ad-Hoc Quality Rule Engines
        foreach (var ruleState in ruleStates)
        {
            if (ruleState.RuleCount == 0 && !ruleState.HasFailure && ruleState.ViolationCount == 0)
            {
                traces.Add(new ValidationEngineTrace(
                    ruleState.Descriptor,
                    nameof(SubmissionProcessOutcome.Skipped),
                    ItemsEvaluated: 0,
                    ViolationCount: 0,
                    DurationMs: ruleState.TotalDurationMs,
                    Summary: hasRules
                        ? $"No quality rules targeting '{ruleState.Descriptor.EngineId}'"
                        : "No quality rules configured"));
                continue;
            }

            var ruleOutcome = ruleState.HasFailure
                ? nameof(SubmissionProcessOutcome.Failed)
                : ruleState.ViolationCount > 0
                    ? nameof(SubmissionProcessOutcome.ValidationErrorsFound)
                    : nameof(SubmissionProcessOutcome.Succeeded);

            var ruleSummary = ruleState.HasFailure
                ? ruleState.FirstIssue ?? "Quality rule engine evaluation failed"
                : ruleState.ViolationCount > 0
                    ? ruleState.FirstIssue ?? $"{ruleState.RuleCount} rule(s); {ruleState.ViolationCount} violation(s)"
                    : BuildFixtureSummary(
                        $"{ruleState.RuleCount} rule(s) verified",
                        validFixtureCount,
                        invalidFixtureCount,
                        ruleState.InvalidTriggers);

            traces.Add(new ValidationEngineTrace(
                ruleState.Descriptor,
                ruleOutcome,
                ItemsEvaluated: ruleState.ItemsEvaluated,
                ViolationCount: ruleState.ViolationCount,
                DurationMs: ruleState.TotalDurationMs,
                Summary: ruleSummary));
        }

        // 3. Identifier Extraction Engine
        if (!identifierEnabled && identifierState.ViolationCount == 0)
        {
            traces.Add(new ValidationEngineTrace(
                identifierState.Descriptor,
                nameof(SubmissionProcessOutcome.Skipped),
                ItemsEvaluated: 0,
                ViolationCount: 0,
                DurationMs: identifierState.TotalDurationMs,
                Summary: hasSchema
                    ? "Identifier extraction disabled by schema"
                    : "No JSON Schema provided for identifier alignment"));
        }
        else
        {
            var idOutcome = identifierState.ViolationCount > 0
                ? nameof(SubmissionProcessOutcome.ValidationErrorsFound)
                : nameof(SubmissionProcessOutcome.Succeeded);

            var idSummary = identifierState.ViolationCount > 0
                ? identifierState.FirstIssue ?? $"{identifierState.ViolationCount} identifier error(s)"
                : BuildFixtureSummary(
                    identifierLabel ?? "Identifier config aligned",
                    validFixtureCount,
                    invalidFixtureCount,
                    identifierState.InvalidTriggers);

            traces.Add(new ValidationEngineTrace(
                identifierState.Descriptor,
                idOutcome,
                ItemsEvaluated: identifierState.ItemsEvaluated,
                ViolationCount: identifierState.ViolationCount,
                DurationMs: identifierState.TotalDurationMs,
                Summary: idSummary));
        }

        return traces;
    }

    private static string BuildFixtureSummary(
        string prefix,
        int validFixtureCount,
        int invalidFixtureCount,
        int invalidTriggers)
    {
        var baseSummary = $"{prefix}; checked {validFixtureCount} valid and {invalidFixtureCount} invalid fixture(s)";
        return invalidTriggers > 0
            ? $"{baseSummary} (triggered {invalidTriggers} invalid fixture(s))"
            : baseSummary;
    }

    private sealed class EngineRunState(ValidationEngineDescriptor descriptor)
    {
        public ValidationEngineDescriptor Descriptor { get; } = descriptor;
        public int RuleCount { get; set; }
        public int ItemsEvaluated { get; set; }
        public int ViolationCount { get; set; }
        public int InvalidTriggers { get; set; }
        public long ElapsedTicks { get; set; }
        public long ExplicitDurationMs { get; set; }
        public bool HasFailure { get; set; }
        public string? FirstIssue { get; set; }

        public long TotalDurationMs
        {
            get { return (long)Stopwatch.GetElapsedTime(0, ElapsedTicks).TotalMilliseconds + ExplicitDurationMs; }
        }
    }

    private static void CollectSchemaErrors(
        EvaluationResults eval,
        string fixtureId,
        List<ValidationMessage> target)
    {
        var added = 0;

        if (eval.Errors is { Count: > 0 })
        {
            var loc = string.IsNullOrWhiteSpace(eval.InstanceLocation.ToString()) ? "/" : eval.InstanceLocation.ToString();
            foreach (var (keyword, msg) in eval.Errors)
            {
                target.Add(ValidationMessage.For(
                    ValidationSources.Schema,
                    msg,
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
                        msg,
                        identifier: fixtureId,
                        path: loc,
                        engineType: typeof(JsonSchema),
                        metadata: new Dictionary<string, object?> { ["keyword"] = keyword }));
                    added++;
                }
            }
        }

        if (added == 0)
        {
            target.Add(ValidationMessage.For(
                ValidationSources.Schema,
                "Payload rejected by JSON Schema.",
                identifier: fixtureId,
                engineType: typeof(JsonSchema)));
        }
    }

    private JsonSchema? ResolveMetaSchema(JsonNode schemaNode, List<ValidationMessage> syntaxErrors)
    {
        if (schemaNode is JsonObject obj && obj.TryGetPropertyValue("$schema", out var schemaUriNode) &&
            schemaUriNode is JsonValue val && val.TryGetValue<string>(out var schemaUri) &&
            !string.IsNullOrWhiteSpace(schemaUri))
        {
            if (_validationOptions.AllowedMetaSchemas.Count > 0 &&
                !_validationOptions.AllowedMetaSchemas.Any(allowed =>
                    string.Equals(allowed.TrimEnd('#'), schemaUri.TrimEnd('#'), StringComparison.OrdinalIgnoreCase)))
            {
                syntaxErrors.Add(ValidationMessage.For(
                    ValidationSources.Schema,
                    $"Meta-schema '{schemaUri}' is not in the configured AllowedMetaSchemas list.",
                    identifier: "$schema",
                    path: "/$schema",
                    engineType: typeof(JsonSchema),
                    metadata: new Dictionary<string, object?> { ["schemaUri"] = schemaUri }));
                return null;
            }

            if (schemaUri.Contains("draft-07", StringComparison.OrdinalIgnoreCase))
            {
                return MetaSchemas.Draft7;
            }
        }

        return MetaSchemas.Draft202012;
    }
}
