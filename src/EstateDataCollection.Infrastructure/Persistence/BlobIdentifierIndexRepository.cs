using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Persistence;

public sealed class BlobIdentifierIndexRepository(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSubmissionRepositoryOptions> options
) : IIdentifierIndexRepository
{
    private const int MaxRetries = 4;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<Result> UpsertEntryAsync(
        string identifierType,
        string identifierValue,
        IdentifierIndexEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifierValue))
        {
            return Result.Success();
        }

        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var blobName = ResolveIndexBlobName(identifierType, identifierValue);
            var blobClient = container.GetBlobClient(blobName);
            var normalizedType = string.IsNullOrWhiteSpace(identifierType) ? "URN" : identifierType.Trim();
            var normalizedValue = identifierValue.Trim();

            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                ETag? existingETag = null;
                List<IdentifierIndexEntry> entries = [];

                if ((await blobClient.ExistsAsync(cancellationToken)).Value)
                {
                    var download = await blobClient.DownloadContentAsync(cancellationToken);
                    existingETag = download.Value.Details.ETag;
                    var existingDoc = JsonSerializer.Deserialize<IdentifierIndexDocument>(download.Value.Content.ToString());
                    if (existingDoc?.Submissions is { Count: > 0 })
                    {
                        entries.AddRange(existingDoc.Submissions);
                    }
                }

                entries.RemoveAll(e => e.SubmissionId == entry.SubmissionId);
                entries.Add(entry);
                entries.Sort((a, b) => b.UpdatedAtUtc.CompareTo(a.UpdatedAtUtc));

                var doc = new IdentifierIndexDocument(
                    IdentifierType: normalizedType,
                    IdentifierValue: normalizedValue,
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Submissions: entries);

                var json = JsonSerializer.Serialize(doc, JsonOptions);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

                var uploadOptions = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                    Conditions = existingETag.HasValue
                        ? new BlobRequestConditions { IfMatch = existingETag.Value }
                        : new BlobRequestConditions { IfNoneMatch = ETag.All }
                };

                try
                {
                    await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
                    return Result.Success();
                }
                catch (RequestFailedException ex) when (
                    attempt < MaxRetries - 1 &&
                    (ex.Status == (int)HttpStatusCode.PreconditionFailed || ex.Status == (int)HttpStatusCode.Conflict))
                {
                    // Retry optimistic concurrency collision cleanly
                }
            }

            return Error.Failure(ErrorCodes.StorageUploadFailed, new { Message = $"Concurrent update conflict on identifier index '{blobName}'." });
        }
        catch (Exception ex)
        {
            return Error.Failure(ErrorCodes.StorageUploadFailed, new { ex.Message });
        }
    }

    public async Task<Result<IReadOnlyList<IdentifierIndexEntry>>> GetEntriesAsync(
        string identifierType,
        string identifierValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifierValue))
        {
            return Array.Empty<IdentifierIndexEntry>();
        }

        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            if (!(await container.ExistsAsync(cancellationToken)).Value)
            {
                return Array.Empty<IdentifierIndexEntry>();
            }

            var blobName = ResolveIndexBlobName(identifierType, identifierValue);
            var blobClient = container.GetBlobClient(blobName);
            if (!(await blobClient.ExistsAsync(cancellationToken)).Value)
            {
                return Array.Empty<IdentifierIndexEntry>();
            }

            var download = await blobClient.DownloadContentAsync(cancellationToken);
            var doc = JsonSerializer.Deserialize<IdentifierIndexDocument>(download.Value.Content.ToString());
            return doc?.Submissions?.ToArray() ?? Array.Empty<IdentifierIndexEntry>();
        }
        catch (Exception ex)
        {
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    public async Task<Result<string?>> ReadSiblingPayloadAsync(
        IdentifierIndexEntry entry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.Equals(entry.Status, "Finalised", StringComparison.OrdinalIgnoreCase))
            {
                var processedContainer = blobServiceClient.GetBlobContainerClient(options.Value.ProcessedContainerName);
                if ((await processedContainer.ExistsAsync(cancellationToken)).Value)
                {
                    var processedPath = !string.IsNullOrWhiteSpace(entry.ProcessedBundlePrefix)
                        ? SubmissionBlobPathResolver.BuildProcessedPayloadBlobName(entry.ProcessedBundlePrefix)
                        : entry.PayloadPath;

                    if (!string.IsNullOrWhiteSpace(processedPath))
                    {
                        var processedBlob = processedContainer.GetBlobClient(processedPath);
                        if ((await processedBlob.ExistsAsync(cancellationToken)).Value)
                        {
                            var dl = await processedBlob.DownloadContentAsync(cancellationToken);
                            return dl.Value.Content.ToString();
                        }
                    }
                }
            }

            // Read draft payload (merged chunks if present, otherwise draft.json)
            var payloadContainer = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            if (!(await payloadContainer.ExistsAsync(cancellationToken)).Value)
            {
                return Result<string?>.Success(null);
            }

            var draftPrefix = SubmissionBlobPathResolver.ResolveDraftPrefix(options.Value.DraftBlobPrefixFormat, entry.SubmissionId);
            var chunks = new List<SubmissionDraftItem>();
            await foreach (var item in payloadContainer.GetBlobsAsync(
                traits: BlobTraits.None,
                states: BlobStates.None,
                prefix: draftPrefix,
                cancellationToken: cancellationToken))
            {
                if (!item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var chunkBlob = payloadContainer.GetBlobClient(item.Name);
                var dl = await chunkBlob.DownloadContentAsync(cancellationToken);
                var content = dl.Value.Content.ToString();
                var ts = item.Properties.CreatedOn ?? item.Properties.LastModified ?? DateTimeOffset.UtcNow;
                chunks.Add(new SubmissionDraftItem(item.Name, ts, content, Encoding.UTF8.GetByteCount(content)));
            }

            if (chunks.Count > 0)
            {
                chunks.Sort((a, b) => string.CompareOrdinal(a.BlobName, b.BlobName));
                var merged = DraftPayloadMerger.Merge(chunks);
                if (merged.IsSuccess)
                {
                    return merged.Value;
                }
            }

            var fallbackDraftPath = !string.IsNullOrWhiteSpace(entry.PayloadPath) && !entry.PayloadPath.Contains("schema=", StringComparison.OrdinalIgnoreCase)
                ? entry.PayloadPath
                : options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId = entry.SubmissionId });

            var draftBlob = payloadContainer.GetBlobClient(fallbackDraftPath);
            if ((await draftBlob.ExistsAsync(cancellationToken)).Value)
            {
                var dl = await draftBlob.DownloadContentAsync(cancellationToken);
                return dl.Value.Content.ToString();
            }

            return Result<string?>.Success(null);
        }
        catch (Exception ex)
        {
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    internal string ResolveIndexBlobName(string identifierType, string identifierValue)
    {
        var prefix = options.Value.IdentifierIndexPrefix.TrimStart('/');
        if (!prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        var rawType = string.IsNullOrWhiteSpace(identifierType) ? "urn" : identifierType.Trim().ToLowerInvariant();
        var safeType = SubmissionBlobPathResolver.SanitiseBlobPathSegment(rawType);
        var safeValue = SubmissionBlobPathResolver.SanitiseBlobPathSegment(identifierValue.Trim());
        return $"{prefix}{safeType}/{safeValue}.json";
    }
}
