using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        try
        {
            var blobName = options.Value.ProcessedBlobNameFormat.FormatWith(submission);
            var payloadsContainer = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            var blobClient = payloadsContainer.GetBlobClient(blobName);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(submission.RawPayloadJson));
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = new Dictionary<string, string>
                {
                    ["submission_id"] = submission.SubmissionId.ToString(),
                    ["urn"] = submission.Urn.ToString(),
                    ["schema_name"] = submission.SchemaName,
                    ["schema_version"] = submission.SchemaVersion.ToString(),
                    ["payload_sha256"] = submission.PayloadSha256Hash,
                    ["submitted_at_utc"] = submission.SubmittedAtUtc.ToString("O")
                }
            };

            await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
            _instrumentation.SubmissionEnvelopeUploaded(submission.SubmissionId, submission.Urn);
            return submission.SubmissionId;
        }
        catch (Exception ex)
        {
            _instrumentation.StorageOperationFailed(nameof(SaveSubmissionAsync), ex.Message);
            return Error.Failure(ErrorCodes.StorageUploadFailed, new { ex.Message });
        }
    }

    public async Task<Result<Guid>> CreateSessionAsync(SubmissionSession session, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            var blobClient =
                container.GetBlobClient(options.Value.SessionBlobNameFormat.FormatWith(new { session.SubmissionId }));

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
            
            await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
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
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);

            var payloadBlob =
                container.GetBlobClient(options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId }));
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

            await payloadBlob.UploadAsync(stream, uploadOptions, cancellationToken);

            if (urn.HasValue && urn.Value > 0)
            {
                var sessionResult = await GetSessionAsync(submissionId, cancellationToken);
                if (sessionResult.IsSuccess && sessionResult.Value.Urn != urn.Value)
                {
                    var session = sessionResult.Value;
                    session.AssignUrn(urn.Value);
                    await CreateSessionAsync(session, cancellationToken);
                }
            }

            _instrumentation.DraftPayloadSaved(submissionId, urn?.ToString() ?? "unassigned");
            return Result.Success();
        }
        catch (Exception ex)
        {
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
        try
        {
            var payloadContainer = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            
            var draftBlob = payloadContainer.GetBlobClient(options.Value.PayloadBlobNameFormat.FormatWith(new { submissionId }));
            
            if (!(await draftBlob.ExistsAsync(cancellationToken)))
            {
                _instrumentation.DraftPayloadNotFound(submissionId);
                return Error.Failure(ErrorCodes.PayloadEmpty, new { submissionId });
            }
            
            var draftContent = (await draftBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(draftContent)));

            var targetBlobName = options.Value.ProcessedBlobNameFormat.FormatWith(new
            {
                urn,
                schemaName,
                schemaVersion,
                submissionId
            });
            
            var processedContainer = blobServiceClient.GetBlobContainerClient(options.Value.ProcessedContainerName);
            var processedBlob = processedContainer.GetBlobClient(targetBlobName);
            
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(draftContent));
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = new Dictionary<string, string>
                {
                    ["submission_id"] = submissionId.ToString(),
                    ["urn"] = urn.ToString(),
                    ["schema_name"] = schemaName,
                    ["schema_version"] = schemaVersion.ToString(),
                    ["payload_sha256"] = sha256,
                    ["status"] = "ReadyForCde",
                    ["finalized_at_utc"] = DateTimeOffset.UtcNow.ToString("O")
                }
            };

            await processedBlob.UploadAsync(stream, uploadOptions, cancellationToken);
            
            var sessionResult = await GetSessionAsync(submissionId, cancellationToken);
            if (sessionResult.IsSuccess)
            {
                var session = sessionResult.Value;
                session.Finalise();
                session.AssignUrn(urn);
                await CreateSessionAsync(session, cancellationToken);
            }

            await draftBlob.DeleteIfExistsAsync(cancellationToken: cancellationToken);

            _instrumentation.PayloadPromotedToProcessed(submissionId, urn, processedBlob.Uri.ToString());
            return processedBlob.Uri.ToString();
        }
        catch (Exception ex)
        {
            _instrumentation.StorageOperationFailed(nameof(PromoteToProcessedAsync), ex.Message);
            return Error.Failure(ErrorCodes.StoragePromotionFailed, new { ex.Message });
        }
    }
}
