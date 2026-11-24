using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Extensions;
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

        if (artifact.IdentifierConfig is not null)
        {
            var idDefValidation = artifact.IdentifierConfig.ValidateDefinition();
            if (idDefValidation.IsFailure)
            {
                var idErrMsg = idDefValidation.Error.ValidationErrors.FirstOrDefault()?.Message
                               ?? idDefValidation.Error.Description
                               ?? "Invalid identifier configuration in draft .meta.json.";
                await schemaRegistry.RecordDraftIngestionFailureAsync(
                    request.BlobPath,
                    artifact.SchemaName,
                    artifact.Version,
                    [idErrMsg],
                    cancellationToken);

                return idDefValidation.Error;
            }
        }

        var resolvedInputs = odcsParser.ResolveInputs(
            artifact.RawSchemaJson,
            artifact.RawRulesJson,
            artifact.RawOdcsYaml,
            request.BlobPath);

        if (resolvedInputs.IsFailure)
        {
            var errMsg = resolvedInputs.Error.ValidationErrors.FirstOrDefault()?.Message
                         ?? resolvedInputs.Error.Description
                         ?? "Failed to parse ODCS YAML contract.";
            await schemaRegistry.RecordDraftIngestionFailureAsync(
                request.BlobPath,
                artifact.SchemaName,
                artifact.Version,
                [errMsg],
                cancellationToken);

            return resolvedInputs.Error;
        }

        var (effectiveSchemaJson, effectiveRulesJson, rawOdcsYaml, metadataContractId, rawVersionText) = resolvedInputs.Value;
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
            if (existingActive.IsSuccess && !string.IsNullOrWhiteSpace(existingActive.Value.Metadata.RawJsonSchema))
            {
                effectiveSchemaJson = existingActive.Value.Metadata.RawJsonSchema;
            }
            else
            {
                // Validate the rules syntax and any fixtures now; if valid, keep staged in draft-schemas until .schema.json lands.
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

        // Verify structural JSON schema + quality rules + identifier alignment + in-flight fixtures
        var schemaParam = string.IsNullOrWhiteSpace(effectiveRulesJson)
            ? (effectiveSchemaJson ?? string.Empty)
            : effectiveSchemaJson;
        var rulesParam = string.IsNullOrWhiteSpace(effectiveRulesJson) ? null : effectiveRulesJson;

        var verification = await schemaVerifier.VerifyCandidateSchemaAsync(
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

        if (!verification.Value.IsSuccessful)
        {
            await schemaRegistry.RecordDraftIngestionFailureAsync(
                request.BlobPath,
                schemaName,
                version,
                verification.Value.Errors,
                cancellationToken);

            var errors = verification.Value.ToValidationErrors("schema").ToList();

            _instrumentation.DraftVerificationFailed(
                request.BlobPath,
                schemaName,
                version,
                errors.Count,
                string.Join("; ", verification.Value.Errors));

            return Error.Validation(ErrorCodes.SchemaVerificationFailed, errors: errors);
        }

        var effectiveIdentifierConfig = verification.Value.ResolvedIdentifierConfig ?? artifact.IdentifierConfig;

        SchemaMetadata metadata;
        try
        {
            metadata = SchemaMetadata.CreateDraft(schemaName, version, effectiveSchemaJson!, effectiveIdentifierConfig);
            // Schema packs ingested from draft-schemas do not carry effective dates;
            // they are promoted in Validated state (EffectiveFromUtc = null, EffectiveToUtc = null)
            // until scheduled via the Admin API.
            metadata.MarkValidated();
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

        var hasFixtures = artifact.SampleValidFixtures.Count > 0 || artifact.SampleInvalidFixtures.Count > 0;
        var saveResult = hasFixtures
            ? await schemaRegistry.SaveSchemaAsync(
                metadata,
                effectiveRulesJson,
                rawOdcsYaml,
                artifact.SampleValidFixtures,
                artifact.SampleInvalidFixtures,
                cancellationToken)
            : (effectiveRulesJson is not null || rawOdcsYaml is not null
                ? await schemaRegistry.SaveSchemaAsync(metadata, effectiveRulesJson, rawOdcsYaml, cancellationToken)
                : await schemaRegistry.SaveSchemaAsync(metadata, cancellationToken));

        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _ = await schemaRegistry.CompleteDraftIngestionAsync(artifact, cancellationToken);
        _instrumentation.DraftSchemaPromoted(request.BlobPath, schemaName, version, metadata.Sha256Hash);

        return new IngestDraftSchemaResult(
            BlobPath: request.BlobPath,
            SchemaName: schemaName,
            Version: version,
            Promoted: true,
            Status: "Promoted",
            State: metadata.State.ToNode(),
            Sha256Hash: metadata.Sha256Hash);
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
