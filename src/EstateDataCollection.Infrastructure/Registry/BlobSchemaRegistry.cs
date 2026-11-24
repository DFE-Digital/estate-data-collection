using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Json.Schema;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Registry;

public sealed class BlobSchemaRegistry(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSchemaRegistryOptions> options,
    IMemoryCache cache,
    BlobSchemaRegistryInstrumentation? instrumentation = null,
    IOdcsContractParser? odcsParser = null) : ISchemaRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BlobSchemaRegistryInstrumentation _instrumentation =
        instrumentation ?? new BlobSchemaRegistryInstrumentation(NullLogger<BlobSchemaRegistry>.Instance);

    private readonly IOdcsContractParser _odcsParser =
        odcsParser ?? new OdcsContractParser();

    public async Task<Result<ResolvedSchema>> ResolveActiveSchemaAsync(string schemaName, DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        var listResult = await ListSchemasAsync(schemaName, cancellationToken);
        if (listResult.IsFailure)
        {
            return listResult.Error;
        }

        if (listResult.Value.Count > 0)
        {
            var activeCandidate = listResult.Value
                .Where(m => m.IsActiveAt(timestamp))
                .OrderByDescending(m => m.Version)
                .FirstOrDefault();

            if (activeCandidate is not null)
            {
                return await GetExplicitSchemaAsync(schemaName, activeCandidate.Version, cancellationToken);
            }

            // If schemas exist for this schemaName, but none is active at the given timestamp, return SchemaNotActive
            return Error.Validation(ErrorCodes.SchemaNotActive, new { schemaName, Version = listResult.Value.Max(m => m.Version) });
        }

        // Fallback to explicit v1 lookup (which returns SchemaNotFound if v1 does not exist)
        var fallback = await GetExplicitSchemaAsync(schemaName, 1, cancellationToken);
        if (fallback.IsSuccess && !fallback.Value.Metadata.IsActiveAt(timestamp))
        {
            return Error.Validation(ErrorCodes.SchemaNotActive, new { schemaName, Version = 1 });
        }

        return fallback;
    }

    public async Task<Result<IReadOnlyList<SchemaMetadata>>> ListSchemasAsync(
        string? schemaNameFilter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
            if (!await schemasContainer.ExistsAsync(cancellationToken))
            {
                return Array.Empty<SchemaMetadata>();
            }

            var prefix = string.IsNullOrWhiteSpace(schemaNameFilter) ? null : $"{schemaNameFilter.Trim().Trim('/')}/";
            var discoveredVersions = new Dictionary<(string Name, int Version), (bool HasSchema, bool HasMeta)>();

            await foreach (var item in schemasContainer.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                if (item.Name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
                {
                    DraftSchemaBlobPathParser.TryParseDraftBlobPathConvention(item.Name, out var sName, out var ver, out _);
                    if (!string.IsNullOrWhiteSpace(sName) && ver is > 0)
                    {
                        var key = (sName, ver.Value);
                        discoveredVersions.TryGetValue(key, out var existing);
                        discoveredVersions[key] = (true, existing.HasMeta);
                    }
                }
                else if (item.Name.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                {
                    var withoutMeta = item.Name[..^".meta.json".Length] + ".schema.json";
                    DraftSchemaBlobPathParser.TryParseDraftBlobPathConvention(withoutMeta, out var sName, out var ver, out _);
                    if (!string.IsNullOrWhiteSpace(sName) && ver is > 0)
                    {
                        var key = (sName, ver.Value);
                        discoveredVersions.TryGetValue(key, out var existing);
                        discoveredVersions[key] = (existing.HasSchema, true);
                    }
                }
            }

            var results = new List<SchemaMetadata>();
            foreach (var ((sName, ver), flags) in discoveredVersions.OrderBy(k => k.Key.Name).ThenBy(k => k.Key.Version))
            {
                if (!flags.HasSchema)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(schemaNameFilter) &&
                    !string.Equals(sName, schemaNameFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var explicitResult = await GetExplicitSchemaAsync(sName, ver, cancellationToken);
                if (explicitResult.IsSuccess)
                {
                    results.Add(explicitResult.Value.Metadata);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    public async Task<Result> UpdateSchemaMetadataAsync(
        SchemaMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName = metadata.Name, version = metadata.Version };
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));

        string? previousMetaSnapshot = null;
        var updatedMetaUploaded = false;

        try
        {
            if (await metaBlob.ExistsAsync(cancellationToken))
            {
                previousMetaSnapshot = (await metaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            }

            var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            using var metaStream = new MemoryStream(Encoding.UTF8.GetBytes(metaJson));
            await metaBlob.UploadAsync(metaStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);
            updatedMetaUploaded = true;

            cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
            _instrumentation.SchemaSavedToBlob(metadata.Name, metadata.Version);

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (updatedMetaUploaded && previousMetaSnapshot is not null)
            {
                try
                {
                    using var restoreStream = new MemoryStream(Encoding.UTF8.GetBytes(previousMetaSnapshot));
                    await metaBlob.UploadAsync(restoreStream, new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                    }, CancellationToken.None);
                }
                catch (Exception rollbackEx)
                {
                    _instrumentation.SchemaSaveToBlobRollbackFailed(metadata.Name, metadata.Version, rollbackEx.Message);
                }
            }

            _instrumentation.SchemaSaveToBlobFailed(metadata.Name, metadata.Version, ex.Message);
            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    public async Task<Result> UpdateSchemaFixturesAsync(
        string schemaName,
        int version,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName, version };
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        var schemaBlob = schemasContainer.GetBlobClient(options.Value.SchemaBlobDirectory.FormatWith(namespacing));

        if (!await schemaBlob.ExistsAsync(cancellationToken))
        {
            _instrumentation.SchemaBlobNotFound(schemaName, version);
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { schemaName, version });
        }

        var validFixturesBlob = schemasContainer.GetBlobClient(options.Value.ValidFixturesBlobDirectory.FormatWith(namespacing));
        var invalidFixturesBlob = schemasContainer.GetBlobClient(options.Value.InvalidFixturesBlobDirectory.FormatWith(namespacing));

        var validExisted = false;
        string? previousValidSnapshot = null;
        var invalidExisted = false;
        string? previousInvalidSnapshot = null;
        var validModified = false;
        var invalidModified = false;

        try
        {
            if (await validFixturesBlob.ExistsAsync(cancellationToken))
            {
                validExisted = true;
                previousValidSnapshot = (await validFixturesBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            }

            if (await invalidFixturesBlob.ExistsAsync(cancellationToken))
            {
                invalidExisted = true;
                previousInvalidSnapshot = (await invalidFixturesBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            }

            if (validFixtures.Count > 0)
            {
                var validJson = DraftSchemaBlobPathParser.SerialiseFixturesArray(validFixtures);
                using var validStream = new MemoryStream(Encoding.UTF8.GetBytes(validJson));
                await validFixturesBlob.UploadAsync(validStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                }, cancellationToken);
                validModified = true;
            }
            else if (validExisted)
            {
                await validFixturesBlob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                validModified = true;
            }

            if (invalidFixtures.Count > 0)
            {
                var invalidJson = DraftSchemaBlobPathParser.SerialiseFixturesArray(invalidFixtures);
                using var invalidStream = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));
                await invalidFixturesBlob.UploadAsync(invalidStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                }, cancellationToken);
                invalidModified = true;
            }
            else if (invalidExisted)
            {
                await invalidFixturesBlob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                invalidModified = true;
            }

            cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
            _instrumentation.SchemaFixturesUpdated(schemaName, version, validFixtures.Count, invalidFixtures.Count);

            return Result.Success();
        }
        catch (Exception ex)
        {
            try
            {
                if (validModified)
                {
                    if (validExisted && previousValidSnapshot is not null)
                    {
                        using var restoreValid = new MemoryStream(Encoding.UTF8.GetBytes(previousValidSnapshot));
                        await validFixturesBlob.UploadAsync(restoreValid, new BlobUploadOptions
                        {
                            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                        }, CancellationToken.None);
                    }
                    else if (!validExisted)
                    {
                        await validFixturesBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                    }
                }

                if (invalidModified)
                {
                    if (invalidExisted && previousInvalidSnapshot is not null)
                    {
                        using var restoreInvalid = new MemoryStream(Encoding.UTF8.GetBytes(previousInvalidSnapshot));
                        await invalidFixturesBlob.UploadAsync(restoreInvalid, new BlobUploadOptions
                        {
                            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                        }, CancellationToken.None);
                    }
                    else if (!invalidExisted)
                    {
                        await invalidFixturesBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                    }
                }
            }
            catch (Exception rollbackEx)
            {
                _instrumentation.SchemaSaveToBlobRollbackFailed(schemaName, version, rollbackEx.Message);
            }

            _instrumentation.SchemaSaveToBlobFailed(schemaName, version, ex.Message);
            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    public async Task<Result<ResolvedSchema>> GetExplicitSchemaAsync(string schemaName, int version, CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName, version };
        var key = options.Value.CacheKey.FormatWith(namespacing);

        if (cache.TryGetValue(key, out ResolvedSchema? cached) && cached is not null)
        {
            _instrumentation.SchemaCacheHit(schemaName, version);
            return cached;
        }

        _instrumentation.SchemaCacheMiss(schemaName, version);

        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);

        var schemaBlob = schemasContainer.GetBlobClient(options.Value.SchemaBlobDirectory.FormatWith(namespacing));
        var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));

        if (!await schemaBlob.ExistsAsync(cancellationToken))
        {
            _instrumentation.SchemaBlobNotFound(schemaName, version);
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { schemaName, version });
        }

        var schemaJson = (await schemaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        var compiledSchema = JsonSchema.FromText(schemaJson);

        SchemaMetadata metadata;
        if (await metaBlob.ExistsAsync(cancellationToken))
        {
            var metaJson = (await metaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            metadata = JsonSerializer.Deserialize<SchemaMetadata>(metaJson, JsonOptions)!;
        }
        else
        {
            // Never default to Active when no .meta.json sidecar exists;
            // keep the schema in Validated (unscheduled) state until explicitly scheduled via Admin API.
            metadata = SchemaMetadata.CreateDraft(schemaName, version, schemaJson);
            metadata.MarkValidated();
        }

        IReadOnlyList<BusinessRule>? rules = null;
        string? rawRulesJson = null;
        var rulesBlob = schemasContainer.GetBlobClient(options.Value.RulesBlobDirectory.FormatWith(namespacing));
        if (await rulesBlob.ExistsAsync(cancellationToken))
        {
            rawRulesJson = (await rulesBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            rules = JsonSerializer.Deserialize<List<BusinessRule>>(rawRulesJson, JsonOptions);
        }
        else if (!string.Equals(options.Value.RulesBlobDirectory, options.Value.LogicBlobDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var logicBlob = schemasContainer.GetBlobClient(options.Value.LogicBlobDirectory.FormatWith(namespacing));
            if (await logicBlob.ExistsAsync(cancellationToken))
            {
                rawRulesJson = (await logicBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
                rules = JsonSerializer.Deserialize<List<BusinessRule>>(rawRulesJson, JsonOptions);
            }
        }

        var validFixtures = await TryLoadFixturesAsync(
            schemasContainer,
            options.Value.ValidFixturesBlobDirectory.FormatWith(namespacing),
            consumedBlobs: null,
            cancellationToken);

        var invalidFixtures = await TryLoadFixturesAsync(
            schemasContainer,
            options.Value.InvalidFixturesBlobDirectory.FormatWith(namespacing),
            consumedBlobs: null,
            cancellationToken);

        var resolved = new ResolvedSchema(metadata, compiledSchema, rules, rawRulesJson, validFixtures, invalidFixtures);
        cache.Set(key, resolved, TimeSpan.FromMinutes(15));
        _instrumentation.SchemaLoadedFromBlob(schemaName, version);

        return resolved;
    }

    public async Task<Result<bool>> ExistsAsync(string schemaName, int version, CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName, version };
        var key = options.Value.CacheKey.FormatWith(namespacing);

        if (cache.TryGetValue(key, out _))
        {
            return true;
        }

        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        var schemaBlob = schemasContainer.GetBlobClient(options.Value.SchemaBlobDirectory.FormatWith(namespacing));

        return (await schemaBlob.ExistsAsync(cancellationToken)).Value;
    }

    public Task<Result> SaveSchemaAsync(SchemaMetadata metadata, CancellationToken cancellationToken = default)
    {
        return SaveSchemaAsync(metadata, null, null, null, null, cancellationToken);
    }

    public Task<Result> SaveSchemaAsync(
        SchemaMetadata metadata,
        string? rawRulesJson,
        string? rawOdcsYaml = null,
        CancellationToken cancellationToken = default)
    {
        return SaveSchemaAsync(metadata, rawRulesJson, rawOdcsYaml, null, null, cancellationToken);
    }

    public async Task<Result> SaveSchemaAsync(
        SchemaMetadata metadata,
        string? rawRulesJson,
        string? rawOdcsYaml,
        IReadOnlyList<string>? sampleValidFixtures,
        IReadOnlyList<string>? sampleInvalidFixtures,
        CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName = metadata.Name, version = metadata.Version };

        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);

        var schemaBlob = schemasContainer.GetBlobClient(options.Value.SchemaBlobDirectory.FormatWith(namespacing));
        var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));
        var rulesBlob = schemasContainer.GetBlobClient(options.Value.RulesBlobDirectory.FormatWith(namespacing));
        var odcsBlob = schemasContainer.GetBlobClient(options.Value.OdcsBlobDirectory.FormatWith(namespacing));
        var validFixturesBlob = schemasContainer.GetBlobClient(options.Value.ValidFixturesBlobDirectory.FormatWith(namespacing));
        var invalidFixturesBlob = schemasContainer.GetBlobClient(options.Value.InvalidFixturesBlobDirectory.FormatWith(namespacing));

        Response<BlobContentInfo>? rawSchemaFileUpload = null;
        Response<BlobContentInfo>? companionMetafieldUpload = null;
        Response<BlobContentInfo>? companionRulesUpload = null;
        Response<BlobContentInfo>? odcsUpload = null;
        Response<BlobContentInfo>? validFixturesUpload = null;
        Response<BlobContentInfo>? invalidFixturesUpload = null;

        try
        {
            // 1. Upload companion metadata file first ({schemaName}/v{version}.meta.json)
            var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            using var metaStream = new MemoryStream(Encoding.UTF8.GetBytes(metaJson));
            companionMetafieldUpload = await metaBlob.UploadAsync(metaStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);

            // 2. Upload companion rules file if present ({schemaName}/v{version}.rules.json)
            if (!string.IsNullOrWhiteSpace(rawRulesJson))
            {
                using var rulesStream = new MemoryStream(Encoding.UTF8.GetBytes(rawRulesJson));
                companionRulesUpload = await rulesBlob.UploadAsync(rulesStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                }, cancellationToken);
            }

            // 3. Upload raw ODCS YAML contract if present ({schemaName}/v{version}.odcs.yaml)
            if (!string.IsNullOrWhiteSpace(rawOdcsYaml))
            {
                using var odcsStream = new MemoryStream(Encoding.UTF8.GetBytes(rawOdcsYaml));
                odcsUpload = await odcsBlob.UploadAsync(odcsStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/x-yaml" }
                }, cancellationToken);
            }

            // 4. Upload stored golden regression test fixtures if provided ({schemaName}/v{version}.valid.json / .invalid.json)
            if (sampleValidFixtures is { Count: > 0 })
            {
                var validJson = DraftSchemaBlobPathParser.SerialiseFixturesArray(sampleValidFixtures);
                using var validStream = new MemoryStream(Encoding.UTF8.GetBytes(validJson));
                validFixturesUpload = await validFixturesBlob.UploadAsync(validStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                }, cancellationToken);
            }

            if (sampleInvalidFixtures is { Count: > 0 })
            {
                var invalidJson = DraftSchemaBlobPathParser.SerialiseFixturesArray(sampleInvalidFixtures);
                using var invalidStream = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));
                invalidFixturesUpload = await invalidFixturesBlob.UploadAsync(invalidStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                }, cancellationToken);
            }

            // 5. Upload raw schema file LAST as the commit marker ({schemaName}/v{version}.schema.json)
            using var schemaStream = new MemoryStream(Encoding.UTF8.GetBytes(metadata.RawJsonSchema));
            rawSchemaFileUpload = await schemaBlob.UploadAsync(schemaStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/schema+json" }
            }, cancellationToken);

            // Invalidate or update local memory cache
            cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
            _instrumentation.SchemaSavedToBlob(metadata.Name, metadata.Version);

            return Result.Success();
        }
        catch (Exception ex)
        {
            try
            {
                if (rawSchemaFileUpload?.HasValue == true)
                {
                    await schemaBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }

                if (companionMetafieldUpload?.HasValue == true)
                {
                    await metaBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }

                if (companionRulesUpload?.HasValue == true)
                {
                    await rulesBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }

                if (odcsUpload?.HasValue == true)
                {
                    await odcsBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }

                if (validFixturesUpload?.HasValue == true)
                {
                    await validFixturesBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }

                if (invalidFixturesUpload?.HasValue == true)
                {
                    await invalidFixturesBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }
            }
            catch (Exception rollbackException)
            {
                // Best-effort compensating rollback; preserve original exception for logging and Result.Failure
                _instrumentation.SchemaSaveToBlobRollbackFailed(metadata.Name, metadata.Version, rollbackException.Message);
            }

            _instrumentation.SchemaSaveToBlobFailed(metadata.Name, metadata.Version, ex.Message);
            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    public async Task<Result<DraftSchemaArtifact>> GetDraftArtefactAsync(
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = DraftSchemaBlobPathParser.NormalizeDraftBlobPath(blobPath, options.Value.DraftContainerName);
        DraftSchemaBlobPathParser.TryParseDraftBlobPathConvention(normalizedPath, out var pathSchemaName, out var pathVersion, out var basePrefix);

        if (DraftSchemaBlobPathParser.IsIgnoredCompanionBlob(normalizedPath, options.Value.DraftErrorBlobSuffix))
        {
            return new DraftSchemaArtifact(
                TriggerBlobPath: normalizedPath,
                SchemaName: pathSchemaName,
                Version: pathVersion,
                RawSchemaJson: null,
                RawRulesJson: null,
                RawOdcsYaml: null,
                SampleValidFixtures: [],
                SampleInvalidFixtures: [],
                ConsumedDraftBlobPaths: [],
                IsIgnoredCompanionBlob: true);
        }

        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var triggerBlob = draftContainer.GetBlobClient(normalizedPath);

        if (!await triggerBlob.ExistsAsync(cancellationToken))
        {
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { blobPath = normalizedPath });
        }

        var content = (await triggerBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        var consumedBlobs = new List<string> { normalizedPath };

        string? rawSchemaJson = null;
        string? rawRulesJson = null;
        string? rawOdcsYaml = null;

        if (_odcsParser.IsOdcsYaml(content, normalizedPath))
        {
            rawOdcsYaml = content;
        }
        else if (DraftSchemaBlobPathParser.IsRulesBlob(normalizedPath, content))
        {
            rawRulesJson = content;

            // Check if companion {basePrefix}.schema.json is also present in draft-schemas
            var companionSchemaPath = $"{basePrefix}.schema.json";
            var companionSchemaBlob = draftContainer.GetBlobClient(companionSchemaPath);
            if (await companionSchemaBlob.ExistsAsync(cancellationToken))
            {
                rawSchemaJson = (await companionSchemaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
                consumedBlobs.Add(companionSchemaPath);
            }
        }
        else
        {
            rawSchemaJson = content;

            // Check if companion {basePrefix}.rules.json or {basePrefix}.logic.json is also present in draft-schemas
            foreach (var rulesSuffix in new[] { ".rules.json", ".logic.json" })
            {
                var companionRulesPath = $"{basePrefix}{rulesSuffix}";
                var companionRulesBlob = draftContainer.GetBlobClient(companionRulesPath);
                if (await companionRulesBlob.ExistsAsync(cancellationToken))
                {
                    rawRulesJson = (await companionRulesBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
                    consumedBlobs.Add(companionRulesPath);
                    break;
                }
            }
        }

        // Check for optional companion metadata file in draft-schemas: {basePrefix}.meta.json
        var metaLoadResult = await TryLoadDraftIdentifierConfigAsync(
            draftContainer,
            $"{basePrefix}.meta.json",
            consumedBlobs,
            cancellationToken);

        if (metaLoadResult.IsFailure)
        {
            await RecordDraftIngestionFailureAsync(
                normalizedPath,
                pathSchemaName,
                pathVersion,
                [metaLoadResult.Error.Description ?? "Malformed draft .meta.json companion file."],
                cancellationToken);
            return metaLoadResult.Error;
        }

        // Check for optional in-flight golden fixture files in draft-schemas: {basePrefix}.valid.json and {basePrefix}.invalid.json
        var validFixtures = await TryLoadFixturesAsync(draftContainer, $"{basePrefix}.valid.json", consumedBlobs, cancellationToken);
        var invalidFixtures = await TryLoadFixturesAsync(draftContainer, $"{basePrefix}.invalid.json", consumedBlobs, cancellationToken);

        return new DraftSchemaArtifact(
            TriggerBlobPath: normalizedPath,
            SchemaName: pathSchemaName,
            Version: pathVersion,
            RawSchemaJson: rawSchemaJson,
            RawRulesJson: rawRulesJson,
            RawOdcsYaml: rawOdcsYaml,
            SampleValidFixtures: validFixtures,
            SampleInvalidFixtures: invalidFixtures,
            ConsumedDraftBlobPaths: consumedBlobs,
            IsIgnoredCompanionBlob: false,
            IdentifierConfig: metaLoadResult.Value);
    }

    public async Task<Result> CompleteDraftIngestionAsync(
        DraftSchemaArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var deletedCount = 0;

        foreach (var path in artifact.ConsumedDraftBlobPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await draftContainer.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                await draftContainer.GetBlobClient($"{path}{options.Value.DraftErrorBlobSuffix}").DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                deletedCount++;
            }
            catch
            {
                // Best-effort cleanup of consumed draft files
            }
        }

        _instrumentation.DraftBlobsCleanedUp(deletedCount, options.Value.DraftContainerName, artifact.TriggerBlobPath);
        return Result.Success();
    }

    public async Task<Result> RecordDraftIngestionFailureAsync(
        string blobPath,
        string? schemaName,
        int? version,
        IReadOnlyList<string> errors,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = DraftSchemaBlobPathParser.NormalizeDraftBlobPath(blobPath, options.Value.DraftContainerName);
        var errorBlobPath = $"{normalizedPath}{options.Value.DraftErrorBlobSuffix}";
        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var errorBlob = draftContainer.GetBlobClient(errorBlobPath);

        Response<BlobContentInfo>? errorBlobUpload = null;
        try
        {
            var diagnosticPayload = new
            {
                blobPath = normalizedPath,
                schemaName,
                version,
                failedAtUtc = DateTimeOffset.UtcNow,
                errors
            };

            var errorJson = JsonSerializer.Serialize(diagnosticPayload, new JsonSerializerOptions { WriteIndented = true });
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(errorJson));
            errorBlobUpload = await errorBlob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);

            _instrumentation.DraftFailureRecorded(errorBlobPath, errors.Count);
            return Result.Success();
        }
        catch (Exception ex)
        {
            try
            {
                if (errorBlobUpload?.HasValue == true)
                {
                    await errorBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }
            }
            catch
            {
                // Best-effort compensating rollback
            }

            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    private static async Task<Result<SchemaIdentifierConfig?>> TryLoadDraftIdentifierConfigAsync(
        BlobContainerClient draftContainer,
        string metaBlobPath,
        List<string> consumedBlobs,
        CancellationToken cancellationToken)
    {
        var blob = draftContainer.GetBlobClient(metaBlobPath);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return (SchemaIdentifierConfig?)null;
        }

        var raw = (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        consumedBlobs.Add(metaBlobPath);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return (SchemaIdentifierConfig?)null;
        }

        try
        {
            var node = JsonNode.Parse(raw);
            if (node is not JsonObject obj)
            {
                return Error.Validation(
                    ErrorCodes.SchemaInvalidIdentifierConfig,
                    new { Message = $"Draft metadata file '{metaBlobPath}' must be a JSON object." });
            }

            JsonNode? configNode = null;
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, "identifierConfig", StringComparison.OrdinalIgnoreCase))
                {
                    configNode = kvp.Value;
                    break;
                }
            }

            if (configNode is not null)
            {
                var parsed = configNode.Deserialize<SchemaIdentifierConfig>(JsonOptions);
                return parsed;
            }

            var hasDirectFields = obj.Any(kvp =>
                string.Equals(kvp.Key, "type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Key, "jsonPointer", StringComparison.OrdinalIgnoreCase));

            if (hasDirectFields)
            {
                var directParsed = obj.Deserialize<SchemaIdentifierConfig>(JsonOptions);
                return directParsed;
            }

            return (SchemaIdentifierConfig?)null;
        }
        catch (Exception ex)
        {
            return Error.Validation(
                ErrorCodes.SchemaInvalidIdentifierConfig,
                new { Message = $"Draft metadata file '{metaBlobPath}' is malformed JSON: {ex.Message}" });
        }
    }

    private static async Task<IReadOnlyList<string>> TryLoadFixturesAsync(
        BlobContainerClient container,
        string fixtureBlobPath,
        List<string>? consumedBlobs,
        CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(fixtureBlobPath);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return [];
        }

        var raw = (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        consumedBlobs?.Add(fixtureBlobPath);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var node = JsonNode.Parse(raw);
            if (node is JsonArray arr)
            {
                return arr
                    .Where(item => item is not null)
                    .Select(item => item is JsonValue v && v.TryGetValue<string>(out var s) ? s : item!.ToJsonString())
                    .ToList();
            }
        }
        catch
        {
            // Fall through and return raw payload so verification reports any syntax issues
        }

        return [raw];
    }
}
