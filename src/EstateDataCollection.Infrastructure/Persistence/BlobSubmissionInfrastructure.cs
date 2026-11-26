using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Persistence;

public class BlobSubmissionInfrastructure(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSubmissionRepositoryOptions> options,
    BlobSubmissionInfrastructureInstrumentation? instrumentation = null,
    IOptions<ValidationOptions>? validationOptions = null,
    IIdentifierIndexRepository? identifierIndexRepository = null
) : ISubmissionInfrastructure
{
    private readonly BlobSubmissionInfrastructureInstrumentation _instrumentation =
        instrumentation ?? new BlobSubmissionInfrastructureInstrumentation(NullLogger<BlobSubmissionInfrastructure>.Instance);

    private readonly string _defaultIdentifierType =
        validationOptions?.Value?.DefaultIdentifierType is { Length: > 0 } dt
            ? dt
            : SchemaIdentifierConfig.DefaultIdentifierType;

    public async Task<Result<Guid>> CreateSessionAsync(SubmissionSession session, CancellationToken cancellationToken = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
        var blobClient =
            container.GetBlobClient(options.Value.SessionBlobNameFormat.FormatWith(new { session.SubmissionId }));

        try
        {
            await UploadSessionBlobAsync(blobClient, session, cancellationToken);
            _instrumentation.SessionCreated(session.SubmissionId, session.SchemaName, session.SchemaVersion);
            return session.SubmissionId;
        }
        catch (Exception ex)
        {
            _instrumentation.StorageOperationFailed(nameof(CreateSessionAsync), ex.Message);
            return Error.Failure(ErrorCodes.StorageUploadFailed, new { ex.Message });
        }
    }

    public async Task<Result<SubmissionSession>> GetSessionAsync(Guid submissionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            var client = container.GetBlobClient(options.Value.SessionBlobNameFormat.FormatWith(new { submissionId }));

            if (!(await client.ExistsAsync(cancellationToken)))
            {
                _instrumentation.SessionNotFound(submissionId);
                return Error.NotFound(ErrorCodes.SubmissionNotFound, new { submissionId });
            }

            var response = await client.DownloadContentAsync(cancellationToken);
            var session = JsonSerializer.Deserialize<SubmissionSession>(response.Value.Content.ToString());

            if (session is null)
            {
                return Error.Failure(ErrorCodes.StorageSerialisationError, new { Message = DomainMessages.SessionMetadataDeserialisationFailed });
            }

            _instrumentation.SessionRetrieved(submissionId);
            return session;
        }
        catch (Exception ex)
        {
            _instrumentation.StorageOperationFailed(nameof(GetSessionAsync), ex.Message);
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    public async Task<Result> SaveDraftPayloadAsync(
        Guid submissionId,
        string rawPayloadJson,
        CancellationToken cancellationToken = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
        var payloadBlob =
            container.GetBlobClient(options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId }));

        string? previousDraftJson = null;
        var hadExistingDraft = false;
        Response<BlobContentInfo>? draftUpload = null;
        BlobClient? newChunkBlob = null;
        var chunkUploaded = false;

        try
        {
            if ((await payloadBlob.ExistsAsync(cancellationToken))?.Value == true)
            {
                var existingDraftResponse = await payloadBlob.DownloadContentAsync(cancellationToken);
                if (existingDraftResponse?.Value is not null)
                {
                    hadExistingDraft = true;
                    previousDraftJson = existingDraftResponse.Value.Content.ToString();
                }
            }

            var existingChunks = await GetDraftChunkItemsSafeAsync(container, submissionId, cancellationToken);

            // Preserve any legacy draft.json as chunk #0 when transitioning to timestamped chunks
            if (existingChunks.Count == 0 && hadExistingDraft && !string.IsNullOrWhiteSpace(previousDraftJson))
            {
                var legacyTimestamp = DateTimeOffset.UtcNow.AddTicks(-1);
                var legacyChunkName = SubmissionBlobPathResolver.BuildTimestampedDraftBlobName(
                    options.Value.DraftBlobPrefixFormat,
                    submissionId,
                    legacyTimestamp,
                    Guid.CreateVersion7());
                var legacyChunkBlob = container.GetBlobClient(legacyChunkName);
                await UploadTimestampedDraftChunkAsync(legacyChunkBlob, submissionId, legacyTimestamp, previousDraftJson, cancellationToken);
                existingChunks =
                [
                    new SubmissionDraftItem(legacyChunkName, legacyTimestamp, previousDraftJson, Encoding.UTF8.GetByteCount(previousDraftJson))
                ];
            }

            var chunkTimestamp = DateTimeOffset.UtcNow;
            var chunkBlobName = SubmissionBlobPathResolver.BuildTimestampedDraftBlobName(
                options.Value.DraftBlobPrefixFormat,
                submissionId,
                chunkTimestamp,
                Guid.CreateVersion7());
            newChunkBlob = container.GetBlobClient(chunkBlobName);
            await UploadTimestampedDraftChunkAsync(newChunkBlob, submissionId, chunkTimestamp, rawPayloadJson, cancellationToken);
            chunkUploaded = true;

            var allPayloads = existingChunks
                .Select(c => c.PayloadJson)
                .Append(rawPayloadJson)
                .ToList();

            var mergedResult = DraftPayloadMerger.MergeJsonPayloads(allPayloads);
            var snapshotJson = mergedResult.IsSuccess ? mergedResult.Value : rawPayloadJson;

            draftUpload = await UploadDraftPayloadBlobAsync(payloadBlob, submissionId, snapshotJson, cancellationToken);

            _instrumentation.DraftPayloadSaved(submissionId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (chunkUploaded && newChunkBlob is not null)
            {
                await SafeDeleteIfExistsAsync(newChunkBlob);
            }

            if (draftUpload?.HasValue == true)
            {
                await RollbackDraftPayloadAsync(payloadBlob, submissionId, hadExistingDraft, previousDraftJson);
            }

            _instrumentation.StorageOperationFailed(nameof(SaveDraftPayloadAsync), ex.Message);
            return Error.Failure(ErrorCodes.StorageUploadFailed, new { ex.Message });
        }
    }

    public async Task<Result<string>> GetDraftPayloadAsync(Guid submissionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            var client = container.GetBlobClient(options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId }));

            if (!(await client.ExistsAsync(cancellationToken)))
            {
                _instrumentation.DraftPayloadNotFound(submissionId);
                return Error.Failure(ErrorCodes.SubmissionNotFound, new { submissionId });
            }

            var chunks = await GetDraftChunkItemsSafeAsync(container, submissionId, cancellationToken);
            if (chunks.Count > 0)
            {
                var merged = DraftPayloadMerger.Merge(chunks);
                if (merged.IsSuccess)
                {
                    _instrumentation.DraftPayloadRetrieved(submissionId);
                    return merged.Value;
                }
            }

            var response = await client.DownloadContentAsync(cancellationToken);
            _instrumentation.DraftPayloadRetrieved(submissionId);
            return response.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            _instrumentation.StorageOperationFailed(nameof(GetDraftPayloadAsync), ex.Message);
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    public async Task<Result<IReadOnlyList<SubmissionDraftItem>>> GetDraftItemsAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            var exists = await container.ExistsAsync(cancellationToken);
            if (exists is null || !exists.Value)
            {
                return Array.Empty<SubmissionDraftItem>();
            }

            var chunks = await GetDraftChunkItemsSafeAsync(container, submissionId, cancellationToken);
            if (chunks.Count > 0)
            {
                return chunks;
            }

            var legacyBlobName = options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId });
            var legacyBlob = container.GetBlobClient(legacyBlobName);
            if ((await legacyBlob.ExistsAsync(cancellationToken))?.Value == true)
            {
                var props = await legacyBlob.GetPropertiesAsync(cancellationToken: cancellationToken);
                var download = await legacyBlob.DownloadContentAsync(cancellationToken);
                var content = download.Value.Content.ToString();
                var timestamp = SubmissionBlobPathResolver.TryReadTimestampFromMetadata(props.Value.Metadata, "last_saved_at_utc")
                                ?? props.Value.LastModified;

                return new SubmissionDraftItem[]
                {
                    new(legacyBlobName, timestamp, content, Encoding.UTF8.GetByteCount(content))
                };
            }

            return Array.Empty<SubmissionDraftItem>();
        }
        catch (Exception ex)
        {
            _instrumentation.StorageOperationFailed(nameof(GetDraftItemsAsync), ex.Message);
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    public async Task<Result<string>> PromoteToProcessedAsync(
        Guid submissionId,
        string? identifierType,
        string? identifierValue,
        string schemaName,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var payloadContainer = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
        var draftBlob = payloadContainer.GetBlobClient(options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId }));
        var sessionBlob = payloadContainer.GetBlobClient(options.Value.SessionBlobNameFormat.FormatWith(new { submissionId }));

        var targetBlobName = SubmissionBlobPathResolver.ResolveProcessedBlobName(
            options.Value.ProcessedBlobNameFormat,
            identifierType,
            identifierValue,
            schemaName,
            schemaVersion,
            submissionId,
            _defaultIdentifierType);

        var bundlePrefix = SubmissionBlobPathResolver.ResolveProcessedBundlePrefix(
            options.Value.ProcessedBlobNameFormat,
            identifierType,
            identifierValue,
            schemaName,
            schemaVersion,
            submissionId,
            _defaultIdentifierType);

        var processedContainer = blobServiceClient.GetBlobContainerClient(options.Value.ProcessedContainerName);
        var processedBlob = processedContainer.GetBlobClient(targetBlobName);

        var uploadedBundleBlobs = new List<BlobClient>();
        SubmissionSession? originalSessionSnapshot = null;
        var sessionUpdated = false;

        try
        {
            if (!(await draftBlob.ExistsAsync(cancellationToken)))
            {
                _instrumentation.DraftPayloadNotFound(submissionId);
                return Error.Failure(ErrorCodes.PayloadEmpty, new { submissionId });
            }

            var chunks = await GetDraftChunkItemsSafeAsync(payloadContainer, submissionId, cancellationToken);
            string draftContent;
            if (chunks.Count > 0)
            {
                var merged = DraftPayloadMerger.Merge(chunks);
                draftContent = merged.IsSuccess
                    ? merged.Value
                    : (await draftBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            }
            else
            {
                draftContent = (await draftBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(draftContent)));

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(draftContent));
            var metadata = new Dictionary<string, string>
            {
                ["submission_id"] = submissionId.ToString(),
                ["schema_name"] = schemaName,
                ["schema_version"] = schemaVersion.ToString(),
                ["payload_sha256"] = sha256,
                ["status"] = "ReadyForCde",
                ["finalized_at_utc"] = DateTimeOffset.UtcNow.ToString("O")
            };

            if (chunks.Count > 0)
            {
                metadata["draft_chunk_count"] = chunks.Count.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(identifierValue))
            {
                metadata["identifier"] = identifierValue.Trim();
                if (!string.IsNullOrWhiteSpace(identifierType))
                {
                    metadata["identifier_type"] = identifierType.Trim();
                }
            }

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = metadata
            };

            var processedUpload = await processedBlob.UploadAsync(stream, uploadOptions, cancellationToken);
            if (processedUpload?.HasValue == true || processedUpload is not null)
            {
                uploadedBundleBlobs.Add(processedBlob);
            }

            // Copy all existing audit entries into processed/{bundlePrefix}audit/
            await CopyExistingAuditBlobsToBundleAsync(
                payloadContainer,
                processedContainer,
                submissionId,
                bundlePrefix,
                uploadedBundleBlobs,
                cancellationToken);

            var sessionResult = await GetSessionAsync(submissionId, cancellationToken);
            if (sessionResult.IsSuccess)
            {
                var session = sessionResult.Value;
                // Keep a pre-finalised snapshot so if any subsequent step fails, session.json is restored to Draft
                originalSessionSnapshot = JsonSerializer.Deserialize<SubmissionSession>(JsonSerializer.Serialize(session));

                session.Finalise(bundlePrefix);
                if (!string.IsNullOrWhiteSpace(identifierValue))
                {
                    session.AssignIdentifier(identifierType, identifierValue);
                }

                var sessionSaveResult = await CreateSessionAsync(session, cancellationToken);
                if (sessionSaveResult.IsFailure)
                {
                    await RollbackBundleBlobsAsync(uploadedBundleBlobs);
                    return sessionSaveResult.Error;
                }
                sessionUpdated = true;

                // Write session envelope copy inside the self-contained processed bundle folder
                var bundleSessionBlob = processedContainer.GetBlobClient(
                    SubmissionBlobPathResolver.BuildProcessedSessionBlobName(bundlePrefix));
                if (bundleSessionBlob is not null)
                {
                    await UploadSessionBlobAsync(bundleSessionBlob, session, cancellationToken);
                    uploadedBundleBlobs.Add(bundleSessionBlob);
                }
            }

            // Best-effort cleanup of the working draft.json snapshot; timestamped chunks under drafts/ remain for Admin inspection
            await SafeDeleteIfExistsAsync(draftBlob);

            if (identifierIndexRepository is not null && !string.IsNullOrWhiteSpace(identifierValue))
            {
                var effectiveType = string.IsNullOrWhiteSpace(identifierType)
                    ? _defaultIdentifierType
                    : identifierType.Trim();

                var indexEntry = new IdentifierIndexEntry(
                    SubmissionId: submissionId,
                    SchemaName: schemaName,
                    SchemaVersion: schemaVersion,
                    Status: "Finalised",
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    PayloadPath: targetBlobName,
                    ProcessedBundlePrefix: bundlePrefix);

                await identifierIndexRepository.UpsertEntryAsync(
                    effectiveType,
                    identifierValue.Trim(),
                    indexEntry,
                    cancellationToken);
            }

            _instrumentation.PayloadPromotedToProcessed(
                submissionId,
                identifierType ?? "none",
                identifierValue ?? "unassigned",
                processedBlob.Uri.ToString());
            return processedBlob.Uri.ToString();
        }
        catch (Exception ex)
        {
            var rollbackTasks = new List<Task>(uploadedBundleBlobs.Count + 1);
            foreach (var uploadedBlob in uploadedBundleBlobs)
            {
                rollbackTasks.Add(SafeDeleteIfExistsAsync(uploadedBlob));
            }

            if (sessionUpdated && originalSessionSnapshot is not null)
            {
                rollbackTasks.Add(SafeRestoreSessionAsync(sessionBlob, originalSessionSnapshot));
            }

            if (rollbackTasks.Count > 0)
            {
                await Task.WhenAll(rollbackTasks);
            }

            _instrumentation.StorageOperationFailed(nameof(PromoteToProcessedAsync), ex.Message);
            return Error.Failure(ErrorCodes.StoragePromotionFailed, new { ex.Message });
        }
    }

    private async Task CopyExistingAuditBlobsToBundleAsync(
        BlobContainerClient payloadContainer,
        BlobContainerClient processedContainer,
        Guid submissionId,
        string bundlePrefix,
        List<BlobClient> uploadedBundleBlobs,
        CancellationToken cancellationToken)
    {
        var auditPrefix = SubmissionBlobPathResolver.ResolveAuditPrefix(options.Value.AuditBlobPrefixFormat, submissionId);
        var blobs = payloadContainer.GetBlobsAsync(traits: BlobTraits.Metadata, states: BlobStates.None, prefix: auditPrefix, cancellationToken: cancellationToken);
        if (blobs is null)
        {
            return;
        }

        await foreach (var blobItem in blobs)
        {
            if (!blobItem.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceBlob = payloadContainer.GetBlobClient(blobItem.Name);
            if (sourceBlob is null)
            {
                continue;
            }

            var download = await sourceBlob.DownloadContentAsync(cancellationToken);
            if (download?.Value is null)
            {
                continue;
            }

            var fileName = blobItem.Name.StartsWith(auditPrefix, StringComparison.OrdinalIgnoreCase)
                ? blobItem.Name[auditPrefix.Length..]
                : Path.GetFileName(blobItem.Name);

            var targetName = SubmissionBlobPathResolver.BuildProcessedAuditBlobName(bundlePrefix, fileName);
            var targetBlob = processedContainer.GetBlobClient(targetName);
            if (targetBlob is null)
            {
                continue;
            }

            var bytes = download.Value.Content.ToArray();
            using var copyStream = new MemoryStream(bytes);
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = blobItem.Metadata is { Count: > 0 }
                    ? new Dictionary<string, string>(blobItem.Metadata)
                    : null
            };

            await targetBlob.UploadAsync(copyStream, uploadOptions, cancellationToken);
            uploadedBundleBlobs.Add(targetBlob);
        }
    }

    private async Task<List<SubmissionDraftItem>> GetDraftChunkItemsSafeAsync(
        BlobContainerClient container,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var prefix = SubmissionBlobPathResolver.ResolveDraftPrefix(options.Value.DraftBlobPrefixFormat, submissionId);
        var blobs = container.GetBlobsAsync(traits: BlobTraits.Metadata, states: BlobStates.None, prefix: prefix, cancellationToken: cancellationToken);
        if (blobs is null)
        {
            return [];
        }

        var items = new List<SubmissionDraftItem>();
        await foreach (var blobItem in blobs)
        {
            if (!blobItem.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var blobClient = container.GetBlobClient(blobItem.Name);
            var download = await blobClient.DownloadContentAsync(cancellationToken);
            var content = download.Value.Content.ToString();
            var createdAt = SubmissionBlobPathResolver.TryReadTimestampFromMetadata(blobItem.Metadata, "created_at_utc")
                            ?? blobItem.Properties.CreatedOn
                            ?? blobItem.Properties.LastModified
                            ?? DateTimeOffset.UtcNow;

            items.Add(new SubmissionDraftItem(
                BlobName: blobItem.Name,
                CreatedAtUtc: createdAt,
                PayloadJson: content,
                SizeBytes: blobItem.Properties.ContentLength ?? Encoding.UTF8.GetByteCount(content)));
        }

        return items
            .OrderBy(i => i.BlobName, StringComparer.Ordinal)
            .ThenBy(i => i.CreatedAtUtc)
            .ToList();
    }

    private static async Task<Response<BlobContentInfo>> UploadSessionBlobAsync(
        BlobClient blobClient,
        SubmissionSession session,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(session);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Metadata = new Dictionary<string, string>
            {
                ["submission_id"] = session.SubmissionId.ToString(),
                ["schema_name"] = session.SchemaName,
                ["schema_version"] = session.SchemaVersion.ToString(),
                ["created_at_utc"] = session.CreatedAtUtc.ToString("O")
            }
        };

        return await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
    }

    private static async Task<Response<BlobContentInfo>> UploadDraftPayloadBlobAsync(
        BlobClient payloadBlob,
        Guid submissionId,
        string rawPayloadJson,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawPayloadJson));
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Metadata = new Dictionary<string, string>
            {
                ["submission_id"] = submissionId.ToString(),
                ["last_saved_at_utc"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        return await payloadBlob.UploadAsync(stream, uploadOptions, cancellationToken);
    }

    private static async Task<Response<BlobContentInfo>> UploadTimestampedDraftChunkAsync(
        BlobClient chunkBlob,
        Guid submissionId,
        DateTimeOffset createdAtUtc,
        string rawPayloadJson,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawPayloadJson));
        var metadata = new Dictionary<string, string>
        {
            ["submission_id"] = submissionId.ToString(),
            ["created_at_utc"] = createdAtUtc.ToUniversalTime().ToString("O")
        };

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Metadata = metadata,
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        };

        return await chunkBlob.UploadAsync(stream, uploadOptions, cancellationToken);
    }

    private static async Task RollbackBundleBlobsAsync(IReadOnlyList<BlobClient> uploadedBlobs)
    {
        if (uploadedBlobs.Count == 0)
        {
            return;
        }

        await Task.WhenAll(uploadedBlobs.Select(SafeDeleteIfExistsAsync));
    }

    private static async Task RollbackDraftPayloadAsync(
        BlobClient payloadBlob,
        Guid submissionId,
        bool hadExistingDraft,
        string? previousDraftJson)
    {
        try
        {
            if (hadExistingDraft && previousDraftJson is not null)
            {
                await UploadDraftPayloadBlobAsync(payloadBlob, submissionId, previousDraftJson, CancellationToken.None);
            }
            else
            {
                await payloadBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
            }
        }
        catch
        {
            // Best-effort compensating rollback
        }
    }

    private static async Task SafeRestoreSessionAsync(BlobClient sessionBlob, SubmissionSession originalSession)
    {
        try
        {
            await UploadSessionBlobAsync(sessionBlob, originalSession, CancellationToken.None);
        }
        catch
        {
            // Best-effort compensating rollback
        }
    }

    private static async Task SafeDeleteIfExistsAsync(BlobClient blobClient)
    {
        try
        {
            await blobClient.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
        }
        catch
        {
            // Best-effort compensating rollback
        }
    }
}
