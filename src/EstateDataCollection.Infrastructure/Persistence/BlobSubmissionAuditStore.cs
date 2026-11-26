using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed class BlobSubmissionAuditStore(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSubmissionRepositoryOptions> options,
    BlobSubmissionAuditStoreInstrumentation? instrumentation = null
) : ISubmissionAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly BlobSubmissionAuditStoreInstrumentation _instrumentation =
        instrumentation ?? new BlobSubmissionAuditStoreInstrumentation(NullLogger<BlobSubmissionAuditStore>.Instance);

    public async Task<Result<string>> AppendAsync(SubmissionAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            var payloadContainer = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            var fileName = SubmissionBlobPathResolver.BuildAuditFileName(entry.TimestampUtc, entry.EventId);
            var prefix = ResolvePrefix(entry.SubmissionId);
            var blobName = $"{prefix}{fileName}";
            var blobClient = payloadContainer.GetBlobClient(blobName);

            var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, JsonOptions));
            using var stream = new MemoryStream(jsonBytes);

            var metadata = new Dictionary<string, string>
            {
                ["submission_id"] = entry.SubmissionId.ToString(),
                ["event_id"] = entry.EventId.ToString(),
                ["action"] = entry.Action,
                ["outcome_status"] = entry.Outcome.Status,
                ["http_status"] = entry.Outcome.HttpStatus.ToString(),
                ["timestamp_utc"] = entry.TimestampUtc.ToUniversalTime().ToString("O")
            };

            if (!string.IsNullOrWhiteSpace(entry.SchemaName))
            {
                metadata["schema_name"] = entry.SchemaName;
            }

            if (entry.SchemaVersion.HasValue)
            {
                metadata["schema_version"] = entry.SchemaVersion.Value.ToString();
            }

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Metadata = metadata,
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            };

            await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);

            // Mirror post-promotion audit entries (e.g. FinaliseSubmission Succeeded written after PromoteToProcessedAsync returns)
            await TryMirrorToProcessedBundleAsync(
                payloadContainer,
                entry.SubmissionId,
                fileName,
                jsonBytes,
                metadata,
                cancellationToken);

            _instrumentation.AuditEntryAppended(
                entry.SubmissionId,
                entry.EventId,
                entry.Action,
                entry.Outcome.Status,
                blobName);

            return blobName;
        }
        catch (Exception ex)
        {
            _instrumentation.AuditAppendFailed(entry.SubmissionId, entry.EventId, ex.Message);
            return Error.Failure(ErrorCodes.StorageUploadFailed, new { ex.Message });
        }
    }

    public async Task<Result<IReadOnlyList<SubmissionAuditEntry>>> ListAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            if (!(await container.ExistsAsync(cancellationToken)).Value)
            {
                return Array.Empty<SubmissionAuditEntry>();
            }

            var prefix = ResolvePrefix(submissionId);
            var entries = new List<SubmissionAuditEntry>();

            await foreach (var blobItem in container.GetBlobsAsync(traits: BlobTraits.None, states: BlobStates.None, prefix: prefix, cancellationToken: cancellationToken))
            {
                if (!blobItem.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var blobClient = container.GetBlobClient(blobItem.Name);
                var download = await blobClient.DownloadContentAsync(cancellationToken);
                var entry = JsonSerializer.Deserialize<SubmissionAuditEntry>(download.Value.Content.ToString(), JsonOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            var ordered = entries
                .OrderBy(e => e.TimestampUtc)
                .ThenBy(e => e.EventId)
                .ToList();

            return ordered;
        }
        catch (Exception ex)
        {
            _instrumentation.AuditListFailed(submissionId, ex.Message);
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    private async Task TryMirrorToProcessedBundleAsync(
        BlobContainerClient payloadContainer,
        Guid submissionId,
        string fileName,
        byte[] jsonBytes,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var sessionBlobName = options.Value.SessionBlobNameFormat.FormatWith(new { submissionId });
        var sessionBlob = payloadContainer.GetBlobClient(sessionBlobName);
        if (sessionBlob is null)
        {
            return;
        }

        var existsResponse = await sessionBlob.ExistsAsync(cancellationToken);
        if (existsResponse?.Value != true)
        {
            return;
        }

        var sessionDownload = await sessionBlob.DownloadContentAsync(cancellationToken);
        if (sessionDownload?.Value is null)
        {
            return;
        }

        var session = JsonSerializer.Deserialize<SubmissionSession>(sessionDownload.Value.Content.ToString());
        if (session is null || string.IsNullOrWhiteSpace(session.ProcessedBundlePrefix))
        {
            return;
        }

        var processedContainer = blobServiceClient.GetBlobContainerClient(options.Value.ProcessedContainerName);
        if (processedContainer is null)
        {
            return;
        }

        var mirrorBlobName = SubmissionBlobPathResolver.BuildProcessedAuditBlobName(session.ProcessedBundlePrefix, fileName);
        var mirrorBlob = processedContainer.GetBlobClient(mirrorBlobName);
        if (mirrorBlob is null)
        {
            return;
        }

        using var mirrorStream = new MemoryStream(jsonBytes);
        var mirrorUploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Metadata = new Dictionary<string, string>(metadata),
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        };

        try
        {
            await mirrorBlob.UploadAsync(mirrorStream, mirrorUploadOptions, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Already mirrored (idempotent)
        }
    }

    private string ResolvePrefix(Guid submissionId)
    {
        return SubmissionBlobPathResolver.ResolveAuditPrefix(options.Value.AuditBlobPrefixFormat, submissionId);
    }
}
