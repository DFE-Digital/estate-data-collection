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
using Json.Schema;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.Validators;

public sealed partial class SchemaVerificationService(
    IBusinessLogicValidator logicValidator,
    IOdcsContractParser? odcsParser = null,
    IOptions<ValidationOptions>? validationOptions = null,
    SchemaVerificationServiceInstrumentation? instrumentation = null,
    ISchemaValidator? schemaValidator = null,
    IContractConsistencyAnalyzer? consistencyAnalyzer = null
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

    private readonly IContractConsistencyAnalyzer _consistencyAnalyzer =
        consistencyAnalyzer ?? new ContractConsistencyAnalyzer();

    public async Task<Result<SchemaVerificationResult>> VerifyCandidateSchemaAsync(
        string? rawSchemaJson,
        string? rawRulesJson,
        IReadOnlyList<string> sampleValidPayloads,
        IReadOnlyList<string> sampleInvalidPayloads,
        SchemaIdentifierConfig? identifierConfig = null,
        CancellationToken cancellationToken = default,
        SchemaValidationPolicy? validationPolicy = null,
        IReadOnlyDictionary<string, JsonNode?>? ambientFixtures = null)
    {
        var syntaxErrors = new List<ValidationMessage>();
        var fixtureErrors = new List<ValidationMessage>();
        var warnings = new List<ValidationMessage>();
        var analyzerFindings = new List<ValidationError>();
        var additionalIssues = new List<ValidationError>();
        var consistencyTrace = new EngineRunState(_consistencyAnalyzer.Descriptor);

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

        if (string.IsNullOrWhiteSpace(rawSchemaJson) && string.IsNullOrWhiteSpace(rawRulesJson))
        {
            return new SchemaVerificationResult(
                [ValidationMessage.For(ValidationSources.Verification, "Either Schema JSON/YAML or Rules JSON must be provided.", identifier: "schema", engineType: typeof(SchemaVerificationService))],
                [],
                EngineTraces: BuildEngineTraces(
                    schemaTrace,
                    ruleTraces.Values,
                    identifierTrace,
                    consistencyTrace,
                    hasSchema: false,
                    hasRules: false,
                    identifierEnabled: false,
                    validFixtureCount: 0,
                    invalidFixtureCount: 0,
                    identifierLabel: null));
        }

        SchemaValidationPolicy? odcsValidationPolicy = null;
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
                        consistencyTrace,
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

        JsonSchema? candidateSchema = null;
        if (hasSchemaInput)
        {
            var syntaxResult = EvaluateSchemaSyntax(rawSchemaJson!, schemaTrace, syntaxErrors, out candidateSchema);
            if (syntaxResult is not null)
            {
                return new SchemaVerificationResult(
                    syntaxResult,
                    [],
                    EngineTraces: BuildEngineTraces(
                        schemaTrace,
                        ruleTraces.Values,
                        identifierTrace,
                        consistencyTrace,
                        hasSchema: true,
                        hasRules: hasRulesInput,
                        identifierEnabled: false,
                        validFixtureCount: 0,
                        invalidFixtureCount: 0,
                        identifierLabel: null));
            }
        }

        IReadOnlyList<BusinessRule>? candidateRules = null;
        if (hasRulesInput)
        {
            EvaluateRulesSyntax(rawRulesJson!, syntaxErrors, GetOrCreateRuleTrace, ruleTraces.Values, out candidateRules);
        }

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

        if (candidateRules is { Count: > 0 })
        {
            RunConsistencyCheck(
                candidateSchema is not null ? rawSchemaJson : null,
                candidateRules,
                consistencyTrace,
                analyzerFindings,
                syntaxErrors,
                warnings);
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
                Warnings: warnings,
                EngineTraces: BuildEngineTraces(
                    schemaTrace,
                    ruleTraces.Values,
                    identifierTrace,
                    consistencyTrace,
                    hasSchema: candidateSchema is not null || hasSchemaInput,
                    hasRules: hasRulesInput,
                    identifierEnabled: identifierAlignment.IsEnabled && candidateSchema is not null,
                    validFixtureCount: 0,
                    invalidFixtureCount: 0,
                    identifierLabel: effectiveIdentifierLabel),
                AnalyzerFindings: analyzerFindings);
        }

        var evalOptions = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = _validationOptions.RequireFormatValidation
        };
        var effectivePolicy = (validationPolicy ?? odcsValidationPolicy ?? SchemaValidationPolicy.Default).Normalise();

        for (var i = 0; i < sampleValidPayloads.Count; i++)
        {
            await EvaluateValidFixtureAsync(
                sampleValidPayloads[i],
                $"SampleValidFixtures:{i}",
                candidateSchema,
                candidateRules,
                identifierAlignment,
                effectiveIdentifierType,
                effectivePolicy,
                evalOptions,
                ambientFixtures,
                schemaTrace,
                identifierTrace,
                ruleTraces.Values,
                GetOrCreateRuleTrace,
                fixtureErrors,
                warnings,
                additionalIssues,
                cancellationToken);
        }

        var invalidFixtureOutcomes = new List<InvalidFixtureOutcome>(sampleInvalidPayloads.Count);
        for (var i = 0; i < sampleInvalidPayloads.Count; i++)
        {
            var outcome = await EvaluateInvalidFixtureAsync(
                i,
                sampleInvalidPayloads[i],
                candidateSchema,
                candidateRules,
                identifierAlignment,
                effectiveIdentifierType,
                effectivePolicy,
                evalOptions,
                ambientFixtures,
                schemaTrace,
                identifierTrace,
                ruleTraces.Values,
                GetOrCreateRuleTrace,
                fixtureErrors,
                warnings,
                cancellationToken);
            invalidFixtureOutcomes.Add(outcome);
        }

        var isSuccess = syntaxErrors.Count == 0 && fixtureErrors.Count == 0;
        _instrumentation.CandidateVerificationCompleted(isSuccess, syntaxErrors.Count, fixtureErrors.Count);

        var traces = BuildEngineTraces(
            schemaTrace,
            ruleTraces.Values,
            identifierTrace,
            consistencyTrace,
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
            EngineTraces: traces,
            AnalyzerFindings: analyzerFindings,
            AdditionalIssues: additionalIssues);
    }

    private IReadOnlyList<ValidationMessage>? EvaluateSchemaSyntax(
        string rawSchemaJson,
        EngineRunState schemaTrace,
        List<ValidationMessage> syntaxErrors,
        out JsonSchema? candidateSchema)
    {
        candidateSchema = null;
        var schemaPhaseStart = Stopwatch.GetTimestamp();
        schemaTrace.ItemsEvaluated++;
        try
        {
            var node = JsonNode.Parse(rawSchemaJson);

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
                    using var schemaDoc = JsonDocument.Parse(rawSchemaJson);
                    var metaResult = meta.Evaluate(schemaDoc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

                    if (!metaResult.IsValid)
                    {
                        var metaErrors = new List<ValidationMessage>();
                        CollectSchemaErrors(metaResult, "schema", metaErrors, addFallbackIfEmpty: false);
                        var errors = metaErrors.Distinct().ToList();
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

                candidateSchema = JsonSchema.FromText(rawSchemaJson);
            }

            return null;
        }
        catch (Exception ex)
        {
            schemaTrace.HasFailure = true;
            schemaTrace.ViolationCount++;
            schemaTrace.FirstIssue ??= ex.Message;

            return
            [
                ValidationMessage.For(ValidationSources.Schema, "Malformed JSON Schema syntax", identifier: "schema", engineType: typeof(JsonSchema)),
                ValidationMessage.For(ValidationSources.Schema, ex.Message, identifier: "schema", engineType: typeof(JsonSchema))
            ];
        }
        finally
        {
            schemaTrace.ElapsedTicks += Stopwatch.GetTimestamp() - schemaPhaseStart;
        }
    }

    private void EvaluateRulesSyntax(
        string rawRulesJson,
        List<ValidationMessage> syntaxErrors,
        Func<string, EngineRunState> getOrCreateRuleTrace,
        IEnumerable<EngineRunState> registeredStates,
        out IReadOnlyList<BusinessRule>? candidateRules)
    {
        var rulesSyntaxStart = Stopwatch.GetTimestamp();
        var logicSyntaxResult = logicValidator.ValidateRulesSyntax(rawRulesJson, out candidateRules);
        var rulesSyntaxTicks = Stopwatch.GetTimestamp() - rulesSyntaxStart;

        if (candidateRules is { Count: > 0 })
        {
            foreach (var ruleGroup in candidateRules.GroupBy(
                         r => r.ResolveEngine(_validationOptions.DefaultEngine),
                         StringComparer.OrdinalIgnoreCase))
            {
                var engineState = getOrCreateRuleTrace(ruleGroup.Key);
                engineState.RuleCount = ruleGroup.Count();
                engineState.ItemsEvaluated += engineState.RuleCount;
                engineState.ElapsedTicks += rulesSyntaxTicks;
            }
        }
        else if (logicSyntaxResult.IsSuccess && logicSyntaxResult.Value.Count > 0)
        {
            RecordRuleSyntaxErrors(
                rawRulesJson,
                logicSyntaxResult.Value,
                rulesSyntaxTicks,
                getOrCreateRuleTrace,
                registeredStates);
        }

        if (logicSyntaxResult.IsSuccess && logicSyntaxResult.Value.Count > 0)
        {
            syntaxErrors.AddRange(logicSyntaxResult.Value);
        }
    }

    private void RunConsistencyCheck(
        string? rawSchemaJson,
        IReadOnlyList<BusinessRule> candidateRules,
        EngineRunState consistencyTrace,
        List<ValidationError> analyzerFindings,
        List<ValidationMessage> syntaxErrors,
        List<ValidationMessage> warnings)
    {
        var consistencyStart = Stopwatch.GetTimestamp();
        var consistencyReport = _consistencyAnalyzer.Analyze(rawSchemaJson, candidateRules);
        consistencyTrace.ElapsedTicks += Stopwatch.GetTimestamp() - consistencyStart;
        consistencyTrace.ItemsEvaluated += candidateRules.Count;

        if (consistencyReport.Findings.Count > 0)
        {
            analyzerFindings.AddRange(consistencyReport.Findings);
        }

        if (consistencyReport.Errors.Count > 0)
        {
            consistencyTrace.ViolationCount += consistencyReport.Errors.Count;
            consistencyTrace.FirstIssue ??= consistencyReport.Errors[0].Message;
            syntaxErrors.AddRange(consistencyReport.Errors);
        }

        if (consistencyReport.Warnings.Count > 0)
        {
            consistencyTrace.WarningCount += consistencyReport.Warnings.Count;
            consistencyTrace.FirstIssue ??= consistencyReport.Warnings[0].Message;
            warnings.AddRange(consistencyReport.Warnings);
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
