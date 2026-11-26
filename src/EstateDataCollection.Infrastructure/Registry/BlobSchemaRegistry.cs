using System.Text;
using System.Text.Json;
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

public sealed partial class BlobSchemaRegistry(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSchemaRegistryOptions> options,
    IMemoryCache cache,
    BlobSchemaRegistryInstrumentation? instrumentation = null,
    IOdcsContractParser? odcsParser = null) : ISchemaRegistry
{
    private static readonly JsonSchema EmptyCompiledSchema = JsonSchema.FromText("{}");

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

            await foreach (var item in schemasContainer.GetBlobsAsync(traits: BlobTraits.None, states: BlobStates.None, prefix: prefix, cancellationToken: cancellationToken))
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
                if (!flags.HasSchema && !flags.HasMeta)
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

        var schemaExists = (await schemaBlob.ExistsAsync(cancellationToken)).Value;
        var metaExists = (await metaBlob.ExistsAsync(cancellationToken)).Value;

        if (!schemaExists && !metaExists)
        {
            _instrumentation.SchemaBlobNotFound(schemaName, version);
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { schemaName, version });
        }

        var schemaJson = schemaExists
            ? (await schemaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString()
            : string.Empty;

        var compiledSchema = CompileSchemaSafe(schemaJson);

        SchemaMetadata metadata;
        if (metaExists)
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
            rules = TryDeserializeRules(rawRulesJson);
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

        var errorReport = await TryLoadErrorReportAsync(
            schemasContainer,
            options.Value.ErrorBlobDirectory.FormatWith(namespacing),
            cancellationToken);

        var resolved = new ResolvedSchema(metadata, compiledSchema, rules, rawRulesJson, validFixtures, invalidFixtures, errorReport);
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
        if ((await schemaBlob.ExistsAsync(cancellationToken)).Value)
        {
            return true;
        }

        var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));
        return (await metaBlob.ExistsAsync(cancellationToken)).Value;
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

        var uploadedBlobs = new List<BlobClient>(6);

        try
        {
            var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await UploadTextBlobAsync(metaBlob, metaJson, "application/json", uploadedBlobs, cancellationToken);

            if (!string.IsNullOrWhiteSpace(rawRulesJson))
            {
                await UploadTextBlobAsync(rulesBlob, rawRulesJson, "application/json", uploadedBlobs, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(rawOdcsYaml))
            {
                await UploadTextBlobAsync(odcsBlob, rawOdcsYaml, "application/x-yaml", uploadedBlobs, cancellationToken);
            }

            if (sampleValidFixtures is { Count: > 0 })
            {
                var validJson = DraftSchemaBlobPathParser.SerialiseFixturesArray(sampleValidFixtures);
                await UploadTextBlobAsync(validFixturesBlob, validJson, "application/json", uploadedBlobs, cancellationToken);
            }

            if (sampleInvalidFixtures is { Count: > 0 })
            {
                var invalidJson = DraftSchemaBlobPathParser.SerialiseFixturesArray(sampleInvalidFixtures);
                await UploadTextBlobAsync(invalidFixturesBlob, invalidJson, "application/json", uploadedBlobs, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(metadata.RawJsonSchema))
            {
                await UploadTextBlobAsync(schemaBlob, metadata.RawJsonSchema, "application/schema+json", uploadedBlobs, cancellationToken);
            }

            cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
            _instrumentation.SchemaSavedToBlob(metadata.Name, metadata.Version);

            return Result.Success();
        }
        catch (Exception ex)
        {
            try
            {
                foreach (var blob in uploadedBlobs)
                {
                    await blob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }
            }
            catch (Exception rollbackException)
            {
                _instrumentation.SchemaSaveToBlobRollbackFailed(metadata.Name, metadata.Version, rollbackException.Message);
            }

            _instrumentation.SchemaSaveToBlobFailed(metadata.Name, metadata.Version, ex.Message);
            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    public async Task<Result<SchemaMetadata>> DeleteSchemaVersionAsync(
        string schemaName,
        int version,
        CancellationToken cancellationToken = default)
    {
        var existingResult = await GetExplicitSchemaAsync(schemaName, version, cancellationToken);
        if (existingResult.IsFailure)
        {
            return existingResult.Error;
        }

        var metadata = existingResult.Value.Metadata;
        metadata.MarkDeleted();

        var metaSave = await UpdateSchemaMetadataAsync(metadata, cancellationToken);
        if (metaSave.IsFailure)
        {
            return metaSave.Error;
        }

        var namespacing = new { schemaName = metadata.Name, version = metadata.Version };
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);

        // Delete contract & fixture blobs while strictly retaining .meta.json and .error.json as tombstones
        var blobsToDelete = new[]
        {
            options.Value.SchemaBlobDirectory.FormatWith(namespacing),
            options.Value.RulesBlobDirectory.FormatWith(namespacing),
            options.Value.OdcsBlobDirectory.FormatWith(namespacing),
            options.Value.ValidFixturesBlobDirectory.FormatWith(namespacing),
            options.Value.InvalidFixturesBlobDirectory.FormatWith(namespacing)
        };

        foreach (var path in blobsToDelete.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await schemasContainer.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }

        cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
        _instrumentation.SchemaVersionDeleted(metadata.Name, metadata.Version);
        return metadata;
    }

    private static async Task UploadTextBlobAsync(
        BlobClient blob,
        string content,
        string contentType,
        List<BlobClient> uploadedBlobs,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var response = await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, cancellationToken);

        if (response?.HasValue != false)
        {
            uploadedBlobs.Add(blob);
        }
    }

    private static JsonSchema CompileSchemaSafe(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return EmptyCompiledSchema;
        }

        try
        {
            return JsonSchema.FromText(schemaJson);
        }
        catch
        {
            return EmptyCompiledSchema;
        }
    }

    private static IReadOnlyList<BusinessRule>? TryDeserializeRules(string rawRulesJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<BusinessRule>>(rawRulesJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
