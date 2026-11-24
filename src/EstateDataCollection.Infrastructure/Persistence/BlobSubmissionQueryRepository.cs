using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed class BlobSubmissionQueryRepository(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSubmissionRepositoryOptions> options,
    BlobSubmissionQueryRepositoryInstrumentation? instrumentation = null
) : ISubmissionQueryRepository
{
    private readonly BlobSubmissionQueryRepositoryInstrumentation _instrumentation =
        instrumentation ?? new BlobSubmissionQueryRepositoryInstrumentation(NullLogger<BlobSubmissionQueryRepository>.Instance);

    private sealed class SubmissionAccumulator
    {
        public string? SessionBlobName { get; set; }
        public IDictionary<string, string>? SessionMetadata { get; set; }
        public bool HasDraftJson { get; set; }
        public int DraftChunkCount { get; set; }
        public DateTimeOffset? LastDraftAtUtc { get; set; }
        public int AuditCount { get; set; }
        public string? LatestAuditBlobName { get; set; }
        public IDictionary<string, string>? LatestAuditMetadata { get; set; }
        public DateTimeOffset? LatestAuditAtUtc { get; set; }
    }

    public async Task<Result<IReadOnlyList<SubmissionSummary>>> ListSubmissionsAsync(
        string? schemaName = null,
        int? schemaVersion = null,
        string? status = null,
        string? searchQuery = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(options.Value.PayloadContainerName);
            if (!(await container.ExistsAsync(cancellationToken)).Value)
            {
                return Array.Empty<SubmissionSummary>();
            }

            var accumulators = new Dictionary<Guid, SubmissionAccumulator>();

            await foreach (var blobItem in container.GetBlobsAsync(
                               traits: BlobTraits.Metadata,
                               cancellationToken: cancellationToken))
            {
                var slashIndex = blobItem.Name.IndexOf('/');
                if (slashIndex <= 0)
                {
                    continue;
                }

                var prefixSegment = blobItem.Name[..slashIndex];
                if (!Guid.TryParse(prefixSegment, out var submissionId))
                {
                    continue;
                }

                if (!accumulators.TryGetValue(submissionId, out var acc))
                {
                    acc = new SubmissionAccumulator();
                    accumulators[submissionId] = acc;
                }

                var relative = blobItem.Name[(slashIndex + 1)..];
                if (string.Equals(relative, "session.json", StringComparison.OrdinalIgnoreCase))
                {
                    acc.SessionBlobName = blobItem.Name;
                    acc.SessionMetadata = blobItem.Metadata;
                }
                else if (string.Equals(relative, "draft.json", StringComparison.OrdinalIgnoreCase))
                {
                    acc.HasDraftJson = true;
                    var draftTs = TryParseTimestamp(blobItem.Metadata, "last_saved_at_utc")
                                  ?? blobItem.Properties.LastModified
                                  ?? blobItem.Properties.CreatedOn;
                    if (draftTs.HasValue && (!acc.LastDraftAtUtc.HasValue || draftTs > acc.LastDraftAtUtc))
                    {
                        acc.LastDraftAtUtc = draftTs;
                    }
                }
                else if (relative.StartsWith("drafts/", StringComparison.OrdinalIgnoreCase) &&
                         relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    acc.DraftChunkCount++;
                    var chunkTs = TryParseTimestamp(blobItem.Metadata, "created_at_utc")
                                  ?? blobItem.Properties.CreatedOn
                                  ?? blobItem.Properties.LastModified;
                    if (chunkTs.HasValue && (!acc.LastDraftAtUtc.HasValue || chunkTs > acc.LastDraftAtUtc))
                    {
                        acc.LastDraftAtUtc = chunkTs;
                    }
                }
                else if (relative.StartsWith("audit/", StringComparison.OrdinalIgnoreCase) &&
                         relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    acc.AuditCount++;
                    var auditTs = TryParseTimestamp(blobItem.Metadata, "timestamp_utc")
                                  ?? blobItem.Properties.CreatedOn
                                  ?? blobItem.Properties.LastModified;

                    if (acc.LatestAuditBlobName is null ||
                        string.Compare(blobItem.Name, acc.LatestAuditBlobName, StringComparison.Ordinal) > 0)
                    {
                        acc.LatestAuditBlobName = blobItem.Name;
                        acc.LatestAuditMetadata = blobItem.Metadata;
                        acc.LatestAuditAtUtc = auditTs;
                    }
                }
            }

            var candidateSessions = accumulators
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.SessionBlobName))
                .Where(kv => MatchesMetadataSchemaFilter(kv.Value.SessionMetadata, schemaName, schemaVersion))
                .ToList();

            var summaries = new List<SubmissionSummary>(candidateSessions.Count);

            foreach (var (submissionId, acc) in candidateSessions)
            {
                var sessionBlob = container.GetBlobClient(acc.SessionBlobName!);
                var download = await sessionBlob.DownloadContentAsync(cancellationToken);
                var session = JsonSerializer.Deserialize<SubmissionSession>(download.Value.Content.ToString());
                if (session is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(schemaName) &&
                    !string.Equals(session.SchemaName, schemaName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (schemaVersion.HasValue && session.SchemaVersion != schemaVersion.Value)
                {
                    continue;
                }

                var effectiveDraftCount = acc.DraftChunkCount > 0
                    ? acc.DraftChunkCount
                    : acc.HasDraftJson ? 1 : 0;

                var hasDraftPayload = !session.IsFinalised && (acc.HasDraftJson || acc.DraftChunkCount > 0);
                var lifecycleState = session.IsFinalised
                    ? "Finalised"
                    : hasDraftPayload
                        ? "Draft"
                        : "AwaitingPayload";

                string? lastAuditAction = null;
                string? lastAuditOutcome = null;
                int? lastAuditHttp = null;

                if (acc.LatestAuditMetadata is not null)
                {
                    acc.LatestAuditMetadata.TryGetValue("action", out lastAuditAction);
                    acc.LatestAuditMetadata.TryGetValue("outcome_status", out lastAuditOutcome);
                    if (acc.LatestAuditMetadata.TryGetValue("http_status", out var rawHttp) &&
                        int.TryParse(rawHttp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHttp))
                    {
                        lastAuditHttp = parsedHttp;
                    }
                }

                var lastActivity = MaxTimestamp(
                    session.CreatedAtUtc,
                    session.FinalisedAtUtc,
                    acc.LastDraftAtUtc,
                    acc.LatestAuditAtUtc);

                var summary = new SubmissionSummary(
                    SubmissionId: submissionId,
                    SchemaName: session.SchemaName,
                    SchemaVersion: session.SchemaVersion,
                    CreatedAtUtc: session.CreatedAtUtc,
                    FinalisedAtUtc: session.FinalisedAtUtc,
                    LastActivityAtUtc: lastActivity,
                    IsFinalised: session.IsFinalised,
                    LifecycleState: lifecycleState,
                    Urn: session.Urn,
                    IdentifierType: session.IdentifierType,
                    IdentifierValue: session.IdentifierValue,
                    HasDraftPayload: hasDraftPayload,
                    DraftCount: effectiveDraftCount,
                    LastDraftAtUtc: acc.LastDraftAtUtc,
                    AuditEventCount: acc.AuditCount,
                    LastAuditAction: lastAuditAction,
                    LastAuditOutcome: lastAuditOutcome,
                    LastAuditHttpStatus: lastAuditHttp,
                    LastAuditAtUtc: acc.LatestAuditAtUtc);

                if (!MatchesStatusFilter(summary, status))
                {
                    continue;
                }

                if (!MatchesSearchQuery(summary, searchQuery))
                {
                    continue;
                }

                summaries.Add(summary);
            }

            return summaries
                .OrderByDescending(s => s.LastActivityAtUtc)
                .ThenByDescending(s => s.SubmissionId)
                .ToList();
        }
        catch (Exception ex)
        {
            _instrumentation.QueryListFailed(ex.Message);
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    public async Task<Result<SubmissionProcessedArtifact?>> GetProcessedArtefactAsync(
        SubmissionSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            var processedContainer = blobServiceClient.GetBlobContainerClient(options.Value.ProcessedContainerName);
            if (!(await processedContainer.ExistsAsync(cancellationToken)).Value)
            {
                return (SubmissionProcessedArtifact?)null;
            }

            var expectedName = ResolveProcessedBlobName(
                session.IdentifierType,
                session.IdentifierValue,
                session.Urn ?? 0,
                session.SchemaName,
                session.SchemaVersion,
                session.SubmissionId);

            var directBlob = processedContainer.GetBlobClient(expectedName);
            if ((await directBlob.ExistsAsync(cancellationToken)).Value)
            {
                return await ReadProcessedArtefactAsync(directBlob, cancellationToken);
            }

            var suffix = $"/{session.SubmissionId}.json";
            await foreach (var blobItem in processedContainer.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if (blobItem.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(blobItem.Name, $"{session.SubmissionId}.json", StringComparison.OrdinalIgnoreCase))
                {
                    var matchBlob = processedContainer.GetBlobClient(blobItem.Name);
                    return await ReadProcessedArtefactAsync(matchBlob, cancellationToken);
                }
            }

            return (SubmissionProcessedArtifact?)null;
        }
        catch (Exception ex)
        {
            _instrumentation.ProcessedLookupFailed(session.SubmissionId, ex.Message);
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    private static async Task<SubmissionProcessedArtifact> ReadProcessedArtefactAsync(
        BlobClient blobClient,
        CancellationToken cancellationToken)
    {
        var props = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        var download = await blobClient.DownloadContentAsync(cancellationToken);
        var content = download.Value.Content.ToString();

        string? sha256 = null;
        string? status = null;
        DateTimeOffset? finalisedAtUtc = null;

        if (props.Value.Metadata is not null)
        {
            props.Value.Metadata.TryGetValue("payload_sha256", out sha256);
            props.Value.Metadata.TryGetValue("status", out status);
            finalisedAtUtc = TryParseTimestamp(props.Value.Metadata, "finalized_at_utc");
        }

        sha256 ??= Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        return new SubmissionProcessedArtifact(
            BlobName: blobClient.Name,
            BlobUri: blobClient.Uri.ToString(),
            PayloadJson: content,
            Sha256Hash: sha256,
            SizeBytes: props.Value.ContentLength,
            FinalisedAtUtc: finalisedAtUtc ?? props.Value.LastModified,
            Status: status);
    }

    private string ResolveProcessedBlobName(
        string? identifierType,
        string? identifierValue,
        int legacyUrn,
        string schemaName,
        int schemaVersion,
        Guid submissionId)
    {
        var format = options.Value.ProcessedBlobNameFormat;
        var effectiveValue = !string.IsNullOrWhiteSpace(identifierValue)
            ? identifierValue.Trim()
            : legacyUrn > 0
                ? legacyUrn.ToString(CultureInfo.InvariantCulture)
                : null;

        if (string.IsNullOrWhiteSpace(effectiveValue))
        {
            format = format
                .Replace("urn={urn}/", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/urn={urn}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .TrimStart('/');

            return format.FormatWith(new
            {
                urn = legacyUrn,
                schemaName,
                schemaVersion,
                submissionId
            });
        }

        var safeValue = SanitiseBlobPathSegment(effectiveValue);
        var effectiveType = string.IsNullOrWhiteSpace(identifierType)
            ? SchemaIdentifierConfig.DefaultUrnType
            : identifierType.Trim();

        if (!string.Equals(effectiveType, SchemaIdentifierConfig.DefaultUrnType, StringComparison.OrdinalIgnoreCase))
        {
            var safeType = SanitiseBlobPathSegment(effectiveType.ToLowerInvariant());
            format = format.Replace("urn={urn}", $"{safeType}={safeValue}", StringComparison.OrdinalIgnoreCase);
        }

        return format.FormatWith(new
        {
            urn = safeValue,
            identifier = safeValue,
            schemaName,
            schemaVersion,
            submissionId
        });
    }

    private static string SanitiseBlobPathSegment(string value)
    {
        return value.Trim()
            .Replace('/', '-')
            .Replace('\\', '-')
            .Replace("..", "_", StringComparison.Ordinal);
    }

    private static bool MatchesMetadataSchemaFilter(
        IDictionary<string, string>? metadata,
        string? schemaName,
        int? schemaVersion)
    {
        if (metadata is null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(schemaName) &&
            metadata.TryGetValue("schema_name", out var metaSchema) &&
            !string.Equals(metaSchema, schemaName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (schemaVersion.HasValue &&
            metadata.TryGetValue("schema_version", out var metaVersion) &&
            int.TryParse(metaVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVersion) &&
            parsedVersion != schemaVersion.Value)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesStatusFilter(SubmissionSummary summary, string? status)
    {
        if (string.IsNullOrWhiteSpace(status) ||
            string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(status, "Finalised", StringComparison.OrdinalIgnoreCase))
        {
            return summary.IsFinalised;
        }

        if (string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase))
        {
            return !summary.IsFinalised;
        }

        if (string.Equals(status, "DraftWithPayload", StringComparison.OrdinalIgnoreCase))
        {
            return !summary.IsFinalised && summary.HasDraftPayload;
        }

        if (string.Equals(status, "AwaitingPayload", StringComparison.OrdinalIgnoreCase))
        {
            return !summary.IsFinalised && !summary.HasDraftPayload;
        }

        return string.Equals(summary.LifecycleState, status.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSearchQuery(SubmissionSummary summary, string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return true;
        }

        var term = searchQuery.Trim();
        return summary.SubmissionId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
               summary.SchemaName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               (summary.IdentifierValue?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (summary.IdentifierType?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (summary.Urn?.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (summary.LastAuditOutcome?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (summary.LastAuditAction?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static DateTimeOffset? TryParseTimestamp(IDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset MaxTimestamp(
        DateTimeOffset createdAt,
        DateTimeOffset? finalisedAt,
        DateTimeOffset? lastDraftAt,
        DateTimeOffset? lastAuditAt)
    {
        var max = createdAt;
        if (finalisedAt.HasValue && finalisedAt.Value > max)
        {
            max = finalisedAt.Value;
        }

        if (lastDraftAt.HasValue && lastDraftAt.Value > max)
        {
            max = lastDraftAt.Value;
        }

        if (lastAuditAt.HasValue && lastAuditAt.Value > max)
        {
            max = lastAuditAt.Value;
        }

        return max;
    }
}
