using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Validation.Enrichment;

public sealed class BlobReferenceDataProvider(
    BlobServiceClient blobServiceClient,
    IMemoryCache memoryCache,
    IOptions<BlobSubmissionRepositoryOptions> repoOptions,
    IOptions<ValidationOptions>? validationOptions = null
) : IReferenceDataProvider
{
    private readonly ValidationOptions _validationOptions = validationOptions?.Value ?? new ValidationOptions();

    public async Task<Result<IReadOnlyDictionary<string, JsonNode?>>> GetReferenceDatasetsAsync(
        SchemaMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var cacheSeconds = _validationOptions.ReferenceDataCacheSeconds;
        var cacheKey = $"refdata:{metadata.Name.ToLowerInvariant()}:v{metadata.Version}";

        if (cacheSeconds > 0 && memoryCache.TryGetValue<IReadOnlyDictionary<string, JsonNode?>>(cacheKey, out var cached) && cached is not null)
        {
            return Result<IReadOnlyDictionary<string, JsonNode?>>.Success(cached);
        }

        try
        {
            var datasets = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

            // 1. Optional local filesystem datasets (used in local dev, verification, or bundled seed data)
            if (!string.IsNullOrWhiteSpace(_validationOptions.ReferenceDataLocalPath) &&
                Directory.Exists(_validationOptions.ReferenceDataLocalPath))
            {
                await LoadFromLocalDirectoryAsync(_validationOptions.ReferenceDataLocalPath, metadata, datasets, cancellationToken);
            }

            // 2. Blob storage container datasets (overlaying local if both exist)
            var containerName = repoOptions.Value.ReferenceDataContainerName;
            if (!string.IsNullOrWhiteSpace(containerName))
            {
                var container = blobServiceClient.GetBlobContainerClient(containerName);
                if ((await container.ExistsAsync(cancellationToken)).Value)
                {
                    await LoadFromBlobContainerAsync(container, metadata, datasets, cancellationToken);
                }
            }

            IReadOnlyDictionary<string, JsonNode?> readOnly = datasets;
            if (cacheSeconds > 0)
            {
                memoryCache.Set(cacheKey, readOnly, TimeSpan.FromSeconds(cacheSeconds));
            }

            return Result<IReadOnlyDictionary<string, JsonNode?>>.Success(readOnly);
        }
        catch (Exception ex)
        {
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    private static async Task LoadFromLocalDirectoryAsync(
        string rootDir,
        SchemaMetadata metadata,
        Dictionary<string, JsonNode?> target,
        CancellationToken ct)
    {
        var candidates = new List<string> { rootDir };
        var schemaDir = Path.Combine(rootDir, metadata.Name);
        if (Directory.Exists(schemaDir))
        {
            candidates.Add(schemaDir);
            var versionDir = Path.Combine(schemaDir, $"v{metadata.Version}");
            if (Directory.Exists(versionDir))
            {
                candidates.Add(versionDir);
            }
        }

        var versionedFlatDir = Path.Combine(rootDir, $"{metadata.Name}-v{metadata.Version}");
        if (Directory.Exists(versionedFlatDir))
        {
            candidates.Add(versionedFlatDir);
        }

        foreach (var dir in candidates)
        {
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var parsed = JsonNode.Parse(content);
                var datasetName = Path.GetFileNameWithoutExtension(file);
                RegisterDatasetKeys(target, datasetName, parsed);
            }
        }
    }

    private static async Task LoadFromBlobContainerAsync(
        BlobContainerClient container,
        SchemaMetadata metadata,
        Dictionary<string, JsonNode?> target,
        CancellationToken ct)
    {
        var schemaPrefix = $"{metadata.Name}/";
        var schemaVersionPrefix = $"{metadata.Name}/v{metadata.Version}/";
        var schemaDashVersionPrefix = $"{metadata.Name}-v{metadata.Version}/";

        var matchedBlobs = new List<(int Priority, string BlobName, string DatasetName)>();

        await foreach (var item in container.GetBlobsAsync(cancellationToken: ct))
        {
            if (!item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.Name.Contains('/'))
            {
                matchedBlobs.Add((0, item.Name, Path.GetFileNameWithoutExtension(item.Name)));
            }
            else if (item.Name.StartsWith(schemaVersionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                matchedBlobs.Add((3, item.Name, Path.GetFileNameWithoutExtension(item.Name)));
            }
            else if (item.Name.StartsWith(schemaDashVersionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                matchedBlobs.Add((2, item.Name, Path.GetFileNameWithoutExtension(item.Name)));
            }
            else if (item.Name.StartsWith(schemaPrefix, StringComparison.OrdinalIgnoreCase) &&
                     item.Name.IndexOf('/', schemaPrefix.Length) < 0)
            {
                matchedBlobs.Add((1, item.Name, Path.GetFileNameWithoutExtension(item.Name)));
            }
        }

        foreach (var entry in matchedBlobs.OrderBy(b => b.Priority).ThenBy(b => b.BlobName, StringComparer.OrdinalIgnoreCase))
        {
            var client = container.GetBlobClient(entry.BlobName);
            var dl = await client.DownloadContentAsync(ct);
            var parsed = JsonNode.Parse(dl.Value.Content.ToString());
            RegisterDatasetKeys(target, entry.DatasetName, parsed);
        }
    }

    private static void RegisterDatasetKeys(Dictionary<string, JsonNode?> target, string rawDatasetName, JsonNode? node)
    {
        if (string.IsNullOrWhiteSpace(rawDatasetName))
        {
            return;
        }

        var trimmed = rawDatasetName.Trim();
        target[trimmed] = node;

        var underscoreName = trimmed.Replace('-', '_');
        if (!string.Equals(underscoreName, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            target[underscoreName] = node?.DeepClone();
        }
    }
}
