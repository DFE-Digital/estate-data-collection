using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Persistence;

public class BlobSubmissionInfrastructure(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSubmissionRepositoryOptions> options,
    BlobSubmissionInfrastructureInstrumentation? instrumentation = null
) : ISubmissionInfrastructure
{
    private readonly BlobSubmissionInfrastructureInstrumentation _instrumentation =
        instrumentation ?? new BlobSubmissionInfrastructureInstrumentation(NullLogger<BlobSubmissionInfrastructure>.Instance);

    public async Task<Result<Guid>> SaveSubmissionAsync(SubmissionEnvelope submission, CancellationToken cancellationToken = default)
    {
        var urnValue = submission.Urn > 0 ? submission.Urn.ToString(CultureInfo.InvariantCulture) : null;
        var blobName = SubmissionBlobPathResolver.ResolveProcessedBlobName(
            options.Value.ProcessedBlobNameFormat,
            urnValue is not null ? SchemaIdentifierConfig.DefaultUrnType : null,
            urnValue,
            submission.Urn,
            submission.SchemaName,
            submission.SchemaVersion,
            submission.SubmissionId);
        var payloadsContainer = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
        var blobClient = payloadsContainer.GetBlobClient(blobName);
        Response<BlobContentInfo>? envelopeUpload = null;

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(submission.RawPayloadJson));
            var metadata = new Dictionary<string, string>
            {
                ["submission_id"] = submission.SubmissionId.ToString(),
                ["schema_name"] = submission.SchemaName,
                ["schema_version"] = submission.SchemaVersion.ToString(),
                ["payload_sha256"] = submission.PayloadSha256Hash,
                ["submitted_at_utc"] = submission.SubmittedAtUtc.ToString("O")
            };

            if (submission.Urn > 0)
            {
                metadata["urn"] = submission.Urn.ToString(CultureInfo.InvariantCulture);
                metadata["identifier"] = submission.Urn.ToString(CultureInfo.InvariantCulture);
                metadata["identifier_type"] = SchemaIdentifierConfig.DefaultUrnType;
            }

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = metadata
            };

            envelopeUpload = await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
            _instrumentation.SubmissionEnvelopeUploaded(submission.SubmissionId, submission.Urn);
            return submission.SubmissionId;
        }
        catch (Exception ex)
        {
            if (envelopeUpload?.HasValue == true)
            {
                await SafeDeleteIfExistsAsync(blobClient);
            }

            _instrumentation.StorageOperationFailed(nameof(SaveSubmissionAsync), ex.Message);
            return Error.Failure(ErrorCodes.StorageUploadFailed, new { ex.Message });
        }
    }

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
                return Error.Failure(ErrorCodes.StorageSerialisationError, new { Message = "Failed to deserialize session metadata." });
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

    public async Task<Result> SaveDraftPayloadAsync(Guid submissionId, int? urn, string rawPayloadJson,
        CancellationToken cancellationToken = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
        var payloadBlob =
            container.GetBlobClient(options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId }));

        string? previousDraftJson = null;
        int? previousDraftUrn = null;
        var hadExistingDraft = false;
        Response<BlobContentInfo>? draftUpload = null;
        BlobClient? newChunkBlob = null;
        var chunkUploaded = false;

        try
        {
            // Snapshot any prior draft payload so a failure during URN session assignment can restore it rather than deleting the user's draft
            if ((await payloadBlob.ExistsAsync(cancellationToken))?.Value == true)
            {
                var existingDraftResponse = await payloadBlob.DownloadContentAsync(cancellationToken);
                if (existingDraftResponse?.Value is not null)
                {
                    hadExistingDraft = true;
                    previousDraftJson = existingDraftResponse.Value.Content.ToString();
                }
            }

            SubmissionSession? sessionToUpdate = null;
            if (urn.HasValue && urn.Value > 0)
            {
                var sessionResult = await GetSessionAsync(submissionId, cancellationToken);
                if (sessionResult.IsSuccess && sessionResult.Value.Urn != urn.Value)
                {
                    sessionToUpdate = sessionResult.Value;
                    previousDraftUrn = sessionToUpdate.Urn;
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
                await UploadTimestampedDraftChunkAsync(legacyChunkBlob, submissionId, legacyTimestamp, previousDraftUrn, previousDraftJson, cancellationToken);
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
            await UploadTimestampedDraftChunkAsync(newChunkBlob, submissionId, chunkTimestamp, urn, rawPayloadJson, cancellationToken);
            chunkUploaded = true;

            var allPayloads = existingChunks
                .Select(c => c.PayloadJson)
                .Append(rawPayloadJson)
                .ToList();

            var mergedResult = DraftPayloadMerger.MergeJsonPayloads(allPayloads);
            var snapshotJson = mergedResult.IsSuccess ? mergedResult.Value : rawPayloadJson;

            draftUpload = await UploadDraftPayloadBlobAsync(payloadBlob, submissionId, urn, snapshotJson, cancellationToken);

            if (sessionToUpdate is not null)
            {
                sessionToUpdate.AssignUrn(urn!.Value);
                var updateSessionResult = await CreateSessionAsync(sessionToUpdate, cancellationToken);
                if (updateSessionResult.IsFailure)
                {
                    if (chunkUploaded && newChunkBlob is not null)
                    {
                        await SafeDeleteIfExistsAsync(newChunkBlob);
                    }
                    await RollbackDraftPayloadAsync(payloadBlob, submissionId, hadExistingDraft, previousDraftJson, previousDraftUrn);
                    return updateSessionResult.Error;
                }
            }

            _instrumentation.DraftPayloadSaved(submissionId, urn?.ToString() ?? "unassigned");
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
                await RollbackDraftPayloadAsync(payloadBlob, submissionId, hadExistingDraft, previousDraftJson, previousDraftUrn);
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

    public Task<Result<string>> PromoteToProcessedAsync(Guid submissionId, int urn, string schemaName, int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var urnValue = urn > 0 ? urn.ToString(CultureInfo.InvariantCulture) : null;
        var urnType = urn > 0 ? SchemaIdentifierConfig.DefaultUrnType : null;
        return PromoteToProcessedCoreAsync(submissionId, urnType, urnValue, urn, schemaName, schemaVersion, cancellationToken);
    }

    public Task<Result<string>> PromoteToProcessedAsync(
        Guid submissionId,
        string? identifierType,
        string? identifierValue,
        string schemaName,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var legacyUrn = int.TryParse(identifierValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;
        return PromoteToProcessedCoreAsync(submissionId, identifierType, identifierValue, legacyUrn, schemaName, schemaVersion, cancellationToken);
    }

    private async Task<Result<string>> PromoteToProcessedCoreAsync(
        Guid submissionId,
        string? identifierType,
        string? identifierValue,
        int legacyUrn,
        string schemaName,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        var payloadContainer = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
        var draftBlob = payloadContainer.GetBlobClient(options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId }));
        var sessionBlob = payloadContainer.GetBlobClient(options.Value.SessionBlobNameFormat.FormatWith(new { submissionId }));

        var targetBlobName = SubmissionBlobPathResolver.ResolveProcessedBlobName(
            options.Value.ProcessedBlobNameFormat,
            identifierType,
            identifierValue,
            legacyUrn,
            schemaName,
            schemaVersion,
            submissionId);

        var processedContainer = blobServiceClient.GetBlobContainerClient(options.Value.ProcessedContainerName);
        var processedBlob = processedContainer.GetBlobClient(targetBlobName);

        Response<BlobContentInfo>? processedUpload = null;
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

            if (legacyUrn > 0)
            {
                metadata["urn"] = legacyUrn.ToString(CultureInfo.InvariantCulture);
            }

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = metadata
            };

            processedUpload = await processedBlob.UploadAsync(stream, uploadOptions, cancellationToken);

            var sessionResult = await GetSessionAsync(submissionId, cancellationToken);
            if (sessionResult.IsSuccess)
            {
                var session = sessionResult.Value;
                // Keep a pre-finalised snapshot so if any subsequent step fails, session.json is restored to Draft
                originalSessionSnapshot = JsonSerializer.Deserialize<SubmissionSession>(JsonSerializer.Serialize(session));

                session.Finalise();
                if (!string.IsNullOrWhiteSpace(identifierValue))
                {
                    session.AssignIdentifier(identifierType, identifierValue);
                }
                else if (legacyUrn > 0)
                {
                    session.AssignUrn(legacyUrn);
                }
                var sessionSaveResult = await CreateSessionAsync(session, cancellationToken);
                if (sessionSaveResult.IsFailure)
                {
                    if (processedUpload?.HasValue == true)
                    {
                        await SafeDeleteIfExistsAsync(processedBlob);
                    }
                    return sessionSaveResult.Error;
                }
                sessionUpdated = true;
            }

            // Best-effort cleanup of the working draft.json snapshot; timestamped chunks under drafts/ remain for Admin inspection
            await SafeDeleteIfExistsAsync(draftBlob);

            _instrumentation.PayloadPromotedToProcessed(submissionId, legacyUrn, processedBlob.Uri.ToString());
            return processedBlob.Uri.ToString();
        }
        catch (Exception ex)
        {
            var rollbackTasks = new List<Task>(2);

            if (processedUpload?.HasValue == true)
            {
                rollbackTasks.Add(SafeDeleteIfExistsAsync(processedBlob));
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

    private async Task<List<SubmissionDraftItem>> GetDraftChunkItemsSafeAsync(
        BlobContainerClient container,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var prefix = SubmissionBlobPathResolver.ResolveDraftPrefix(options.Value.DraftBlobPrefixFormat, submissionId);
        var blobs = container.GetBlobsAsync(traits: BlobTraits.Metadata, prefix: prefix, cancellationToken: cancellationToken);
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
        int? urn,
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
                ["urn"] = urn?.ToString() ?? "unassigned",
                ["last_saved_at_utc"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        return await payloadBlob.UploadAsync(stream, uploadOptions, cancellationToken);
    }

    private static async Task<Response<BlobContentInfo>> UploadTimestampedDraftChunkAsync(
        BlobClient chunkBlob,
        Guid submissionId,
        DateTimeOffset createdAtUtc,
        int? urn,
        string rawPayloadJson,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawPayloadJson));
        var metadata = new Dictionary<string, string>
        {
            ["submission_id"] = submissionId.ToString(),
            ["created_at_utc"] = createdAtUtc.ToUniversalTime().ToString("O")
        };

        if (urn.HasValue && urn.Value > 0)
        {
            metadata["urn"] = urn.Value.ToString(CultureInfo.InvariantCulture);
        }

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Metadata = metadata,
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        };

        return await chunkBlob.UploadAsync(stream, uploadOptions, cancellationToken);
    }

    private static async Task RollbackDraftPayloadAsync(
        BlobClient payloadBlob,
        Guid submissionId,
        bool hadExistingDraft,
        string? previousDraftJson,
        int? previousDraftUrn)
    {
        try
        {
            if (hadExistingDraft && previousDraftJson is not null)
            {
                await UploadDraftPayloadBlobAsync(payloadBlob, submissionId, previousDraftUrn, previousDraftJson, CancellationToken.None);
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
