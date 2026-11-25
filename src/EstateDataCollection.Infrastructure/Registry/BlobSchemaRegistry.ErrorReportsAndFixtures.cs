using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Infrastructure.Registry;

public sealed partial class BlobSchemaRegistry
{
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
        var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));

        if (!await schemaBlob.ExistsAsync(cancellationToken) && !await metaBlob.ExistsAsync(cancellationToken))
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

            validModified = await SyncFixtureBlobAsync(validFixturesBlob, validFixtures, validExisted, cancellationToken);
            invalidModified = await SyncFixtureBlobAsync(invalidFixturesBlob, invalidFixtures, invalidExisted, cancellationToken);

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
                    await RestoreFixtureSnapshotAsync(validFixturesBlob, validExisted, previousValidSnapshot);
                }

                if (invalidModified)
                {
                    await RestoreFixtureSnapshotAsync(invalidFixturesBlob, invalidExisted, previousInvalidSnapshot);
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

    public async Task<Result> SaveErrorReportAsync(
        SchemaErrorReport report,
        CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName = report.SchemaName, version = report.Version };
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        var errorBlobPath = options.Value.ErrorBlobDirectory.FormatWith(namespacing);
        var errorBlob = schemasContainer.GetBlobClient(errorBlobPath);

        try
        {
            await schemasContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await errorBlob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);

            cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
            _instrumentation.SchemaErrorReportSaved(report.SchemaName, report.Version, errorBlobPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _instrumentation.SchemaSaveToBlobFailed(report.SchemaName, report.Version, ex.Message);
            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    public async Task<Result<SchemaErrorReport?>> GetErrorReportAsync(
        string schemaName,
        int version,
        CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName, version };
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        if (!await schemasContainer.ExistsAsync(cancellationToken))
        {
            return (SchemaErrorReport?)null;
        }

        return await TryLoadErrorReportAsync(
            schemasContainer,
            options.Value.ErrorBlobDirectory.FormatWith(namespacing),
            cancellationToken);
    }

    public async Task<Result> DeleteErrorReportAsync(
        string schemaName,
        int version,
        CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName, version };
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        var errorBlob = schemasContainer.GetBlobClient(options.Value.ErrorBlobDirectory.FormatWith(namespacing));

        await errorBlob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
        return Result.Success();
    }

    private static async Task<bool> SyncFixtureBlobAsync(
        BlobClient blob,
        IReadOnlyList<string> fixtures,
        bool existedBefore,
        CancellationToken cancellationToken)
    {
        if (fixtures.Count > 0)
        {
            var json = DraftSchemaBlobPathParser.SerialiseFixturesArray(fixtures);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);
            return true;
        }

        if (existedBefore)
        {
            await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            return true;
        }

        return false;
    }

    private static async Task RestoreFixtureSnapshotAsync(
        BlobClient blob,
        bool existedBefore,
        string? previousSnapshot)
    {
        if (existedBefore && previousSnapshot is not null)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(previousSnapshot));
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, CancellationToken.None);
        }
        else if (!existedBefore)
        {
            await blob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
        }
    }

    private static async Task<SchemaErrorReport?> TryLoadErrorReportAsync(
        BlobContainerClient container,
        string errorBlobPath,
        CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(errorBlobPath);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        try
        {
            var raw = (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SchemaErrorReport>(raw, JsonOptions);
        }
        catch
        {
            return null;
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
