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
    SchemaVerificationServiceInstrumentation? instrumentation = null
) : ISchemaVerificationService
{
    private readonly ValidationOptions _validationOptions = validationOptions?.Value ?? new ValidationOptions();
    private readonly SchemaVerificationServiceInstrumentation _instrumentation =
        instrumentation ?? new SchemaVerificationServiceInstrumentation(NullLogger<SchemaVerificationService>.Instance);

    public async Task<Result<SchemaVerificationResult>> VerifyCandidateSchemaAsync(
        string? rawSchemaJson,
        string? rawRulesJson,
        IReadOnlyList<string> sampleValidPayloads,
        IReadOnlyList<string> sampleInvalidPayloads,
        SchemaIdentifierConfig? identifierConfig = null,
        CancellationToken cancellationToken = default)
    {
        var syntaxErrors = new List<ValidationMessage>();
        var fixtureErrors = new List<ValidationMessage>();
        var warnings = new List<ValidationMessage>();

        JsonSchema? candidateSchema = null;
        IReadOnlyList<BusinessRule>? candidateRules = null;

        if (string.IsNullOrWhiteSpace(rawSchemaJson) && string.IsNullOrWhiteSpace(rawRulesJson))
        {
            return new SchemaVerificationResult(
                [ValidationMessage.For(ValidationSources.Verification, "Either Schema JSON/YAML or Rules JSON must be provided.", identifier: "schema", engineType: typeof(SchemaVerificationService))],
                []);
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
                    []);
            }

            rawSchemaJson = odcsResult.Value.GeneratedSchemaJson;
            if (string.IsNullOrWhiteSpace(rawRulesJson) && !string.IsNullOrWhiteSpace(odcsResult.Value.GeneratedRulesJson))
            {
                rawRulesJson = odcsResult.Value.GeneratedRulesJson;
            }
        }

        // Phase One A: Syntax & Meta-Schema Evaluation (Draft 2020-12 & Draft-07)
        if (!string.IsNullOrWhiteSpace(rawSchemaJson))
        {
            try
            {
                var node = JsonNode.Parse(rawSchemaJson);

                if (node is null)
                {
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
                        var metaResult = meta.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

                        if (!metaResult.IsValid)
                        {
                            var errors = metaResult.Details
                                .Where(x => x.HasErrors && x.Errors is { Count: > 0 })
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
                                })
                                .Distinct();

                            syntaxErrors.AddRange(errors);
                        }
                    }

                    candidateSchema = JsonSchema.FromText(rawSchemaJson);
                }
            }
            catch (Exception ex)
            {
                return new SchemaVerificationResult(
                    [
                        ValidationMessage.For(ValidationSources.Schema, "Malformed JSON Schema syntax", identifier: "schema", engineType: typeof(JsonSchema)),
                        ValidationMessage.For(ValidationSources.Schema, ex.Message, identifier: "schema", engineType: typeof(JsonSchema))
                    ],
                    []);
            }
        }

        // Phase One B: Syntax & Engine Validation for Quality Rules
        if (!string.IsNullOrWhiteSpace(rawRulesJson))
        {
            var logicSyntaxResult = logicValidator.ValidateRulesSyntax(rawRulesJson, out candidateRules);
            if (logicSyntaxResult.IsSuccess && logicSyntaxResult.Value.Count > 0)
            {
                syntaxErrors.AddRange(logicSyntaxResult.Value);
            }
        }

        // Phase One C: Identifier Config Alignment Against Schema AST & Derived Requiredness
        var identifierAlignment = IdentifierSchemaValidator.ValidateAndAlign(
            candidateSchema is not null ? rawSchemaJson : null,
            identifierConfig,
            _validationOptions.UrnJsonPaths,
            _validationOptions.RequireUrnOnFinalise);

        if (!identifierAlignment.IsValid)
        {
            syntaxErrors.AddRange(identifierAlignment.Errors);
        }

        if (syntaxErrors.Count > 0)
        {
            _instrumentation.CandidateVerificationCompleted(false, syntaxErrors.Count, 0);
            return new SchemaVerificationResult(syntaxErrors, [], ResolvedIdentifierConfig: identifierAlignment.EffectiveConfig);
        }

        // Phase Two: In-Flight Golden Fixture Dry-Runs (against Schema, Quality Rules, and Identifier Extraction)
        var evalOptions = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = _validationOptions.RequireFormatValidation
        };

        var effectiveIdentifierType = identifierAlignment.EffectiveConfig?.Type ?? SchemaIdentifierConfig.DefaultUrnType;

        // Ensure valid fixtures pass schema, quality rules, and identifier extraction
        for (var i = 0; i < sampleValidPayloads.Count; i++)
        {
            var payload = sampleValidPayloads[i];
            var fixtureId = $"SampleValidFixtures:{i}";
            var payloadParsed = true;

            if (candidateSchema is not null)
            {
                try
                {
                    var validNode = JsonNode.Parse(payload);
                    var eval = candidateSchema.Evaluate(validNode, evalOptions);

                    if (!eval.IsValid)
                    {
                        fixtureErrors.Add(ValidationMessage.For(
                            ValidationSources.Schema,
                            $"{fixtureId} was expected to pass, but was rejected by the schema.",
                            identifier: fixtureId,
                            engineType: typeof(JsonSchema)));

                        fixtureErrors.AddRange((eval.Details?.Where(x => x.HasErrors && x.Errors is { Count: > 0 }) ?? [])
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
                    fixtureErrors.Add(ValidationMessage.For(
                        ValidationSources.Json,
                        $"{fixtureId} could not be parsed: {ex.Message}",
                        identifier: fixtureId,
                        engineType: typeof(JsonSchema)));
                }
            }

            if (candidateRules is { Count: > 0 })
            {
                var logicResult = await logicValidator.ValidateAsync(payload, candidateRules, cancellationToken);
                if (logicResult.IsFailure)
                {
                    fixtureErrors.Add(ValidationMessage.For(
                        ValidationSources.Rule,
                        $"{fixtureId} quality rule evaluation failed: {logicResult.Error.Description}",
                        identifier: fixtureId,
                        engineType: logicValidator.GetType(),
                        metadata: new Dictionary<string, object?> { ["errorCode"] = logicResult.Error.Code }));
                }
                else if (!logicResult.Value.IsValid)
                {
                    var errors = logicResult.Value.Errors ?? [];
                    var ruleErrors = string.Join("; ", errors.Select(e => $"{e.PropertyPath}: {e.Message}"));
                    var ruleIds = errors.Select(e => e.RuleId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
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
            }

            if (payloadParsed && identifierAlignment.IsEnabled && candidateSchema is not null)
            {
                var idInspect = SubmissionIdentifier.InspectPayload(
                    payload,
                    identifierAlignment.ResolvedPaths,
                    effectiveIdentifierType);

                if (idInspect.IsSuccess)
                {
                    if (idInspect.Value.Status == IdentifierExtractionStatus.Invalid)
                    {
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
                            ?? "/urn";
                        fixtureErrors.Add(ValidationMessage.For(
                            ValidationSources.Identifier,
                            $"{fixtureId} is missing required '{effectiveIdentifierType}' identifier at '{expectedPath}'.",
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
                try
                {
                    var invalidNode = JsonNode.Parse(payload);
                    var eval = candidateSchema.Evaluate(invalidNode, evalOptions);
                    if (!eval.IsValid)
                    {
                        schemaFailed = true;
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
                    triggeredErrors.Add(ValidationMessage.For(
                        ValidationSources.Json,
                        $"Could not parse payload: {ex.Message}",
                        identifier: fixtureId,
                        engineType: typeof(JsonNode)));
                }
            }

            if (candidateRules is { Count: > 0 })
            {
                var logicResult = await logicValidator.ValidateAsync(payload, candidateRules, cancellationToken);
                if (logicResult.IsFailure)
                {
                    rulesFailed = true;
                    triggeredErrors.Add(ValidationMessage.For(
                        ValidationSources.Rule,
                        logicResult.Error.Description ?? logicResult.Error.Code,
                        identifier: fixtureId,
                        engineType: logicValidator.GetType(),
                        metadata: new Dictionary<string, object?> { ["errorCode"] = logicResult.Error.Code }));
                }
                else if (!logicResult.Value.IsValid)
                {
                    rulesFailed = true;
                    foreach (var err in logicResult.Value.Errors ?? [])
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

            if (identifierAlignment.IsEnabled && candidateSchema is not null)
            {
                var idInspect = SubmissionIdentifier.InspectPayload(
                    payload,
                    identifierAlignment.ResolvedPaths,
                    effectiveIdentifierType);

                if (idInspect.IsSuccess)
                {
                    if (idInspect.Value.Status == IdentifierExtractionStatus.Invalid)
                    {
                        identifierFailed = true;
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
                        var expectedPath = idInspect.Value.MatchedPath
                            ?? identifierAlignment.ResolvedPaths.FirstOrDefault()
                            ?? "/urn";
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
        return new SchemaVerificationResult(
            syntaxErrors,
            fixtureErrors,
            ResolvedIdentifierConfig: identifierAlignment.EffectiveConfig,
            InvalidFixtureOutcomes: invalidFixtureOutcomes,
            Warnings: warnings);
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
            foreach (var detail in eval.Details.Where(d => d.HasErrors && d.Errors is { Count: > 0 }))
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
