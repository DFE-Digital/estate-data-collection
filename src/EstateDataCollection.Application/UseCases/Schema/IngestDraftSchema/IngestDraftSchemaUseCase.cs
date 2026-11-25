using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Extensions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;

public sealed partial class IngestDraftSchemaUseCase(
    ISchemaRegistry schemaRegistry,
    ISchemaVerificationService schemaVerifier,
    IOdcsContractParser? odcsParser = null,
    IngestDraftSchemaUseCaseInstrumentation? instrumentation = null
) : IUseCase<IngestDraftSchemaResult, IngestDraftSchemaRequest>
{
    private readonly IngestDraftSchemaUseCaseInstrumentation _instrumentation =
        instrumentation ?? new IngestDraftSchemaUseCaseInstrumentation(NullLogger<IngestDraftSchemaUseCase>.Instance);

    public async Task<Result<IngestDraftSchemaResult>> ExecuteAsync(
        IngestDraftSchemaRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BlobPath))
        {
            return Error.Failure(ErrorCodes.PayloadEmpty, "Draft blob path cannot be empty.");
        }

        _instrumentation.IngestingDraftBlob(request.BlobPath);

        var artifactResult = await schemaRegistry.GetDraftArtefactAsync(request.BlobPath, cancellationToken);
        if (artifactResult.IsFailure)
        {
            return artifactResult.Error;
        }

        var artifact = artifactResult.Value;
        if (artifact.IsIgnoredCompanionBlob)
        {
            _instrumentation.SkippingCompanionBlob(request.BlobPath);
            return new IngestDraftSchemaResult(
                BlobPath: request.BlobPath,
                SchemaName: artifact.SchemaName ?? string.Empty,
                Version: artifact.Version ?? 0,
                Promoted: false,
                Status: "IgnoredCompanionBlob");
        }

        string? prelimSchemaId = null;
        int? prelimVersion = null;
        if (!string.IsNullOrWhiteSpace(artifact.RawSchemaJson))
        {
            ExtractJsonSchemaMetadata(artifact.RawSchemaJson, out prelimSchemaId, out prelimVersion);
        }

        var fallbackSchemaName = ResolveSchemaName(artifact.SchemaName, prelimSchemaId, request.BlobPath);
        var fallbackVersion = artifact.Version ?? prelimVersion ?? 1;

        if (artifact.IdentifierConfig is not null)
        {
            var idDefValidation = artifact.IdentifierConfig.ValidateDefinition();
            if (idDefValidation.IsFailure)
            {
                var conflict = await CheckConsumedConflictAsync(fallbackSchemaName, fallbackVersion, cancellationToken);
                if (conflict is not null)
                {
                    return conflict;
                }

                return await PersistInvalidDraftAsync(
                    request.BlobPath,
                    artifact,
                    fallbackSchemaName,
                    fallbackVersion,
                    artifact.RawSchemaJson,
                    artifact.RawRulesJson,
                    artifact.RawOdcsYaml,
                    validationPolicy: null,
                    idDefValidation.Error,
                    "identifierConfig",
                    cancellationToken);
            }
        }

        var resolvedInputs = odcsParser.ResolveInputs(
            artifact.RawSchemaJson,
            artifact.RawRulesJson,
            artifact.RawOdcsYaml,
            request.BlobPath);

        if (resolvedInputs.IsFailure)
        {
            var conflict = await CheckConsumedConflictAsync(fallbackSchemaName, fallbackVersion, cancellationToken);
            if (conflict is not null)
            {
                return conflict;
            }

            return await PersistInvalidDraftAsync(
                request.BlobPath,
                artifact,
                fallbackSchemaName,
                fallbackVersion,
                artifact.RawSchemaJson,
                artifact.RawRulesJson,
                artifact.RawOdcsYaml,
                validationPolicy: null,
                resolvedInputs.Error,
                "odcs",
                cancellationToken);
        }

        var (effectiveSchemaJson, effectiveRulesJson, rawOdcsYaml, metadataContractId, rawVersionText, parsedValidationPolicy) = resolvedInputs.Value;
        var metadataVersion = TryParseMajorVersion(rawVersionText);

        // JSON schema metadata takes precedence over ODCS YAML metadata when both are present
        if (!string.IsNullOrWhiteSpace(effectiveSchemaJson) && !string.IsNullOrWhiteSpace(artifact.RawSchemaJson))
        {
            ExtractJsonSchemaMetadata(effectiveSchemaJson, out var jsonSchemaId, out var jsonVersion);
            metadataContractId = !string.IsNullOrWhiteSpace(jsonSchemaId) ? jsonSchemaId : metadataContractId;
            metadataVersion = jsonVersion ?? metadataVersion;
        }
        else if (rawOdcsYaml is null && !string.IsNullOrWhiteSpace(effectiveSchemaJson))
        {
            ExtractJsonSchemaMetadata(effectiveSchemaJson, out metadataContractId, out metadataVersion);
        }

        var schemaName = ResolveSchemaName(artifact.SchemaName, metadataContractId, request.BlobPath);
        var version = artifact.Version ?? metadataVersion ?? 1;

        // If a standalone .rules.json / .logic.json file arrived and no companion .schema.json was in draft-schemas,
        // check whether the active schemas container already has {schemaName}/v{version}.schema.json.
        if (string.IsNullOrWhiteSpace(effectiveSchemaJson) && !string.IsNullOrWhiteSpace(effectiveRulesJson))
        {
            var existingActive = await schemaRegistry.GetExplicitSchemaAsync(schemaName, version, cancellationToken);
            if (existingActive.IsSuccess)
            {
                if (existingActive.Value.Metadata.IsDeleted)
                {
                    return Error.Conflict(ErrorCodes.SchemaAlreadyExists, new { schemaName, version });
                }

                if (!string.IsNullOrWhiteSpace(existingActive.Value.Metadata.RawJsonSchema))
                {
                    effectiveSchemaJson = existingActive.Value.Metadata.RawJsonSchema;
                }
            }
            else
            {
                var rulesOnlyVerification = await schemaVerifier.VerifyCandidateSchemaAsync(
                    null,
                    effectiveRulesJson,
                    artifact.SampleValidFixtures,
                    artifact.SampleInvalidFixtures,
                    identifierConfig: null,
                    cancellationToken);

                if (rulesOnlyVerification.IsFailure)
                {
                    return rulesOnlyVerification.Error;
                }

                if (!rulesOnlyVerification.Value.IsSuccessful)
                {
                    await schemaRegistry.RecordDraftIngestionFailureAsync(
                        request.BlobPath,
                        schemaName,
                        version,
                        rulesOnlyVerification.Value.Errors,
                        cancellationToken);

                    var errors = rulesOnlyVerification.Value.ToValidationErrors("rules").ToList();
                    _instrumentation.DraftVerificationFailed(
                        request.BlobPath,
                        schemaName,
                        version,
                        errors.Count,
                        string.Join("; ", rulesOnlyVerification.Value.Errors));

                    return Error.Validation(ErrorCodes.SchemaVerificationFailed, errors: errors);
                }

                _instrumentation.DraftRulesStagedAwaitingSchema(request.BlobPath, schemaName, version);
                return new IngestDraftSchemaResult(
                    BlobPath: request.BlobPath,
                    SchemaName: schemaName,
                    Version: version,
                    Promoted: false,
                    Status: "StagedAwaitingSchema");
            }
        }
        else
        {
            var conflict = await CheckConsumedConflictAsync(schemaName, version, cancellationToken);
            if (conflict is not null)
            {
                return conflict;
            }
        }

        // Verify structural JSON schema + quality rules + identifier alignment + in-flight fixtures
        var schemaParam = string.IsNullOrWhiteSpace(effectiveRulesJson)
            ? (effectiveSchemaJson ?? string.Empty)
            : effectiveSchemaJson;
        var rulesParam = string.IsNullOrWhiteSpace(effectiveRulesJson) ? null : effectiveRulesJson;

        var verification = parsedValidationPolicy is not null
            ? await schemaVerifier.VerifyCandidateSchemaAsync(
                schemaParam,
                rulesParam,
                artifact.SampleValidFixtures,
                artifact.SampleInvalidFixtures,
                artifact.IdentifierConfig,
                cancellationToken,
                parsedValidationPolicy)
            : await schemaVerifier.VerifyCandidateSchemaAsync(
                schemaParam,
                rulesParam,
                artifact.SampleValidFixtures,
                artifact.SampleInvalidFixtures,
                artifact.IdentifierConfig,
                cancellationToken);

        if (verification.IsFailure)
        {
            return verification.Error;
        }

        var report = verification.Value;
        var effectiveIdentifierConfig = report.ResolvedIdentifierConfig ?? artifact.IdentifierConfig;

        if (!report.IsSuccessful)
        {
            var errorReport = report.ToErrorReport(schemaName, version);
            _instrumentation.DraftVerificationFailed(
                request.BlobPath,
                schemaName,
                version,
                report.Errors.Count,
                string.Join("; ", report.Errors));

            var invalidMeta = SchemaMetadata.CreateInvalid(
                schemaName,
                version,
                effectiveSchemaJson ?? artifact.RawSchemaJson,
                effectiveIdentifierConfig,
                parsedValidationPolicy,
                errorReport.Summary);

            var saveInvalid = await SaveDraftTargetAsync(invalidMeta, effectiveRulesJson, rawOdcsYaml, artifact, cancellationToken);
            if (saveInvalid.IsFailure)
            {
                return saveInvalid.Error;
            }

            var errSave = await schemaRegistry.SaveErrorReportAsync(errorReport, cancellationToken);
            if (errSave.IsFailure)
            {
                return errSave.Error;
            }

            _ = await schemaRegistry.CompleteDraftIngestionAsync(artifact, cancellationToken);
            return new IngestDraftSchemaResult(
                BlobPath: request.BlobPath,
                SchemaName: schemaName,
                Version: version,
                Promoted: false,
                Status: "Invalid",
                State: invalidMeta.State.ToNode(),
                Sha256Hash: invalidMeta.Sha256Hash,
                HasErrorReport: true,
                ErrorSummary: invalidMeta.ErrorSummary,
                Issues: errorReport.Issues);
        }

        SchemaMetadata metadata;
        SchemaErrorReport? warningReport = null;
        try
        {
            metadata = SchemaMetadata.CreateDraft(schemaName, version, effectiveSchemaJson!, effectiveIdentifierConfig, parsedValidationPolicy);
            if (report.HasWarnings)
            {
                warningReport = report.ToErrorReport(schemaName, version);
                metadata.MarkUnvalidated(warningReport.Summary);
            }
            else
            {
                metadata.MarkValidated();
            }
        }
        catch (Exception ex)
        {
            await schemaRegistry.RecordDraftIngestionFailureAsync(
                request.BlobPath,
                schemaName,
                version,
                [ex.Message],
                cancellationToken);

            return Error.Validation(ErrorCodes.SchemaInvalidSchedule, ex.Message);
        }

        var saveResult = await SaveDraftTargetAsync(metadata, effectiveRulesJson, rawOdcsYaml, artifact, cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        if (warningReport is not null)
        {
            var errSave = await schemaRegistry.SaveErrorReportAsync(warningReport, cancellationToken);
            if (errSave.IsFailure)
            {
                return errSave.Error;
            }
        }

        _ = await schemaRegistry.CompleteDraftIngestionAsync(artifact, cancellationToken);
        _instrumentation.DraftSchemaPromoted(request.BlobPath, schemaName, version, metadata.Sha256Hash);

        return new IngestDraftSchemaResult(
            BlobPath: request.BlobPath,
            SchemaName: schemaName,
            Version: version,
            Promoted: true,
            Status: metadata.State == SchemaLifecycleState.Unvalidated ? "PromotedUnvalidated" : "Promoted",
            State: metadata.State.ToNode(),
            Sha256Hash: metadata.Sha256Hash,
            HasErrorReport: metadata.HasErrorReport,
            ErrorSummary: metadata.ErrorSummary,
            Issues: warningReport?.Issues ?? report.Issues);
    }

    private async Task<Error?> CheckConsumedConflictAsync(
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        var exists = await schemaRegistry.ExistsAsync(schemaName, version, cancellationToken);
        if (exists.IsFailure)
        {
            return exists.Error;
        }

        if (exists.Value)
        {
            return Error.Conflict(ErrorCodes.SchemaAlreadyExists, new { schemaName, version });
        }

        return null;
    }

    private async Task<Result<IngestDraftSchemaResult>> PersistInvalidDraftAsync(
        string blobPath,
        DraftSchemaArtifact artifact,
        string schemaName,
        int version,
        string? rawSchemaJson,
        string? rawRulesJson,
        string? rawOdcsYaml,
        SchemaValidationPolicy? validationPolicy,
        Error error,
        string defaultPath,
        CancellationToken cancellationToken)
    {
        var errs = error.ValidationErrors is { Count: > 0 }
            ? error.ValidationErrors.Select(e => e with { Severity = RuleSeverity.Error }).ToArray()
            : [new ValidationError(defaultPath, error.Description ?? error.Code, Severity: RuleSeverity.Error)];

        var issues = ValidationIssuesHelper.GroupBySeverity(errs);
        var messages = errs.Select(e => e.Message).ToArray();
        var summary = string.Join("; ", messages.Take(3));

        _instrumentation.DraftVerificationFailed(blobPath, schemaName, version, errs.Length, summary);

        var metadata = SchemaMetadata.CreateInvalid(
            schemaName,
            version,
            rawSchemaJson,
            artifact.IdentifierConfig,
            validationPolicy,
            summary);

        var saveResult = await SaveDraftTargetAsync(metadata, rawRulesJson, rawOdcsYaml, artifact, cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        var report = new SchemaErrorReport(
            SchemaName: schemaName,
            Version: version,
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Issues: issues,
            SyntaxErrors: messages,
            FixtureErrors: [],
            Warnings: [],
            Summary: summary);

        var errSave = await schemaRegistry.SaveErrorReportAsync(report, cancellationToken);
        if (errSave.IsFailure)
        {
            return errSave.Error;
        }

        _ = await schemaRegistry.CompleteDraftIngestionAsync(artifact, cancellationToken);
        return new IngestDraftSchemaResult(
            BlobPath: blobPath,
            SchemaName: schemaName,
            Version: version,
            Promoted: false,
            Status: "Invalid",
            State: metadata.State.ToNode(),
            Sha256Hash: metadata.Sha256Hash,
            HasErrorReport: true,
            ErrorSummary: metadata.ErrorSummary,
            Issues: issues);
    }

    private Task<Result> SaveDraftTargetAsync(
        SchemaMetadata metadata,
        string? effectiveRulesJson,
        string? rawOdcsYaml,
        DraftSchemaArtifact artifact,
        CancellationToken cancellationToken)
    {
        var hasFixtures = artifact.SampleValidFixtures.Count > 0 || artifact.SampleInvalidFixtures.Count > 0;
        return hasFixtures
            ? schemaRegistry.SaveSchemaAsync(
                metadata,
                effectiveRulesJson,
                rawOdcsYaml,
                artifact.SampleValidFixtures,
                artifact.SampleInvalidFixtures,
                cancellationToken)
            : (effectiveRulesJson is not null || rawOdcsYaml is not null
                ? schemaRegistry.SaveSchemaAsync(metadata, effectiveRulesJson, rawOdcsYaml, cancellationToken)
                : schemaRegistry.SaveSchemaAsync(metadata, cancellationToken));
    }

    private static string ResolveSchemaName(string? pathSchemaName, string? metadataContractId, string blobPath)
    {
        if (!string.IsNullOrWhiteSpace(pathSchemaName))
        {
            return pathSchemaName;
        }

        if (!string.IsNullOrWhiteSpace(metadataContractId))
        {
            return metadataContractId;
        }

        var fileName = Path.GetFileName(blobPath);
        foreach (var suffix in new[] { ".odcs.yaml", ".odcs.yml", ".schema.json", ".rules.json", ".logic.json", ".yaml", ".yml", ".json" })
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^suffix.Length];
                break;
            }
        }

        return string.IsNullOrWhiteSpace(fileName) ? "unknown-schema" : fileName;
    }

    private static void ExtractJsonSchemaMetadata(string rawSchemaJson, out string? schemaId, out int? version)
    {
        schemaId = null;
        version = null;

        try
        {
            if (JsonNode.Parse(rawSchemaJson) is not JsonObject obj)
            {
                return;
            }

            if (obj.TryGetPropertyValue("$id", out var idNode) && idNode is JsonValue idVal && idVal.TryGetValue<string>(out var rawId) && !string.IsNullOrWhiteSpace(rawId))
            {
                schemaId = ExtractNameFromIdOrUri(rawId);
            }
            else if (obj.TryGetPropertyValue("id", out var legacyIdNode) && legacyIdNode is JsonValue legacyVal && legacyVal.TryGetValue<string>(out var legacyId) && !string.IsNullOrWhiteSpace(legacyId))
            {
                schemaId = ExtractNameFromIdOrUri(legacyId);
            }
            else if (obj.TryGetPropertyValue("title", out var titleNode) && titleNode is JsonValue titleVal && titleVal.TryGetValue<string>(out var title) && !string.IsNullOrWhiteSpace(title))
            {
                schemaId = Slugify(title);
            }

            if (obj.TryGetPropertyValue("version", out var verNode) && verNode is JsonValue verVal)
            {
                if (verVal.TryGetValue<int>(out var intVer) && intVer > 0)
                {
                    version = intVer;
                }
                else if (verVal.TryGetValue<string>(out var strVer))
                {
                    version = TryParseMajorVersion(strVer);
                }
            }
        }
        catch
        {
            // Ignore metadata extraction errors; syntax validation will report malformed JSON
        }
    }

    private static string ExtractNameFromIdOrUri(string rawId)
    {
        var trimmed = rawId.Trim().TrimEnd('#', '/');
        var lastSlash = trimmed.LastIndexOf('/');
        var segment = lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;

        foreach (var suffix in new[] { ".schema.json", ".odcs.yaml", ".json", ".yaml" })
        {
            if (segment.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                segment = segment[..^suffix.Length];
                break;
            }
        }

        return segment;
    }

    private static string Slugify(string value)
    {
        var slug = NonAlphanumericRegex().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? value.Trim() : slug;
    }

    private static int? TryParseMajorVersion(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }

        var trimmed = versionText.Trim().TrimStart('v', 'V');
        var dotIndex = trimmed.IndexOf('.');
        var majorPart = dotIndex > 0 ? trimmed[..dotIndex] : trimmed;

        return int.TryParse(majorPart, out var major) && major > 0 ? major : null;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();
}
