using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
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
        var blobName = ResolveProcessedBlobName(submission.Urn, submission.SchemaName, submission.SchemaVersion, submission.SubmissionId);
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
                metadata["urn"] = submission.Urn.ToString();
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
        var sessionBlob =
            container.GetBlobClient(options.Value.SessionBlobNameFormat.FormatWith(new { submissionId }));

        string? previousDraftJson = null;
        int? previousDraftUrn = null;
        var hadExistingDraft = false;
        Response<BlobContentInfo>? draftUpload = null;

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

            draftUpload = await UploadDraftPayloadBlobAsync(payloadBlob, submissionId, urn, rawPayloadJson, cancellationToken);

            if (sessionToUpdate is not null)
            {
                sessionToUpdate.AssignUrn(urn!.Value);
                var updateSessionResult = await CreateSessionAsync(sessionToUpdate, cancellationToken);
                if (updateSessionResult.IsFailure)
                {
                    await RollbackDraftPayloadAsync(payloadBlob, submissionId, hadExistingDraft, previousDraftJson, previousDraftUrn);
                    return updateSessionResult.Error;
                }
            }

            _instrumentation.DraftPayloadSaved(submissionId, urn?.ToString() ?? "unassigned");
            return Result.Success();
        }
        catch (Exception ex)
        {
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

    public async Task<Result<string>> PromoteToProcessedAsync(Guid submissionId, int urn, string schemaName, int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var payloadContainer = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
        var draftBlob = payloadContainer.GetBlobClient(options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId }));
        var sessionBlob = payloadContainer.GetBlobClient(options.Value.SessionBlobNameFormat.FormatWith(new { submissionId }));

        var targetBlobName = ResolveProcessedBlobName(urn, schemaName, schemaVersion, submissionId);

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
            
            var draftContent = (await draftBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
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

            if (urn > 0)
            {
                metadata["urn"] = urn.ToString();
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
                if (urn > 0)
                {
                    session.AssignUrn(urn);
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

            // Best-effort cleanup of the source draft payload now that processedBlob and session.json are committed
            await SafeDeleteIfExistsAsync(draftBlob);

            _instrumentation.PayloadPromotedToProcessed(submissionId, urn, processedBlob.Uri.ToString());
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

    private string ResolveProcessedBlobName(int urn, string schemaName, int schemaVersion, Guid submissionId)
    {
        var format = options.Value.ProcessedBlobNameFormat;
        if (urn <= 0)
        {
            format = format
                .Replace("urn={urn}/", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/urn={urn}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .TrimStart('/');
        }

        return format.FormatWith(new
        {
            urn,
            schemaName,
            schemaVersion,
            submissionId
        });
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
