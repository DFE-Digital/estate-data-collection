using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Infrastructure.Registry;

public sealed partial class BlobSchemaRegistry
{
    public async Task<Result<DraftSchemaArtifact>> GetDraftArtefactAsync(
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        var normalisedPath = DraftSchemaBlobPathParser.NormaliseDraftBlobPath(blobPath, options.Value.DraftContainerName);
        DraftSchemaBlobPathParser.TryParseDraftBlobPathConvention(normalisedPath, out var pathSchemaName, out var pathVersion, out var basePrefix);

        if (DraftSchemaBlobPathParser.IsIgnoredCompanionBlob(normalisedPath, options.Value.DraftErrorBlobSuffix))
        {
            return new DraftSchemaArtifact(
                TriggerBlobPath: normalisedPath,
                SchemaName: pathSchemaName,
                Version: pathVersion,
                RawSchemaJson: null,
                RawRulesJson: null,
                RawOdcsYaml: null,
                SampleValidFixtures: [],
                SampleInvalidFixtures: [],
                ConsumedDraftBlobPaths: [],
                IsIgnoredCompanionBlob: true);
        }

        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var triggerBlob = draftContainer.GetBlobClient(normalisedPath);

        if (!await triggerBlob.ExistsAsync(cancellationToken))
        {
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { blobPath = normalisedPath });
        }

        var content = (await triggerBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        var consumedBlobs = new List<string> { normalisedPath };

        string? rawSchemaJson = null;
        string? rawRulesJson = null;
        string? rawOdcsYaml = null;

        if (_odcsParser.IsOdcsYaml(content, normalisedPath))
        {
            rawOdcsYaml = content;
        }
        else if (DraftSchemaBlobPathParser.IsRulesBlob(normalisedPath, content))
        {
            rawRulesJson = content;
        }
        else
        {
            rawSchemaJson = content;
        }

        rawSchemaJson ??= await TryLoadCompanionAsync(draftContainer, basePrefix, [".schema.json"], consumedBlobs, cancellationToken);
        rawRulesJson ??= await TryLoadCompanionAsync(draftContainer, basePrefix, [".rules.json"], consumedBlobs, cancellationToken);
        rawOdcsYaml ??= await TryLoadCompanionAsync(draftContainer, basePrefix, [".odcs.yaml", ".odcs.yml", ".yaml", ".yml"], consumedBlobs, cancellationToken);

        var metaLoadResult = await TryLoadDraftIdentifierConfigAsync(
            draftContainer,
            $"{basePrefix}.meta.json",
            consumedBlobs,
            cancellationToken);

        if (metaLoadResult.IsFailure)
        {
            await RecordDraftIngestionFailureAsync(
                normalisedPath,
                pathSchemaName,
                pathVersion,
                [metaLoadResult.Error.Description ?? "Malformed draft .meta.json companion file."],
                cancellationToken);
            return metaLoadResult.Error;
        }

        var validFixtures = await TryLoadFixturesAsync(draftContainer, $"{basePrefix}.valid.json", consumedBlobs, cancellationToken);
        var invalidFixtures = await TryLoadFixturesAsync(draftContainer, $"{basePrefix}.invalid.json", consumedBlobs, cancellationToken);

        return new DraftSchemaArtifact(
            TriggerBlobPath: normalisedPath,
            SchemaName: pathSchemaName,
            Version: pathVersion,
            RawSchemaJson: rawSchemaJson,
            RawRulesJson: rawRulesJson,
            RawOdcsYaml: rawOdcsYaml,
            SampleValidFixtures: validFixtures,
            SampleInvalidFixtures: invalidFixtures,
            ConsumedDraftBlobPaths: consumedBlobs,
            IsIgnoredCompanionBlob: false,
            IdentifierConfig: metaLoadResult.Value);
    }

    public async Task<Result> CompleteDraftIngestionAsync(
        DraftSchemaArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var deletedCount = 0;

        foreach (var path in artifact.ConsumedDraftBlobPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await draftContainer.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                await draftContainer.GetBlobClient($"{path}{options.Value.DraftErrorBlobSuffix}").DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                deletedCount++;
            }
            catch
            {
                // Best-effort cleanup of consumed draft files
            }
        }

        _instrumentation.DraftBlobsCleanedUp(deletedCount, options.Value.DraftContainerName, artifact.TriggerBlobPath);
        return Result.Success();
    }

    public async Task<Result> RecordDraftIngestionFailureAsync(
        string blobPath,
        string? schemaName,
        int? version,
        IReadOnlyList<string> errors,
        CancellationToken cancellationToken = default)
    {
        var normalisedPath = DraftSchemaBlobPathParser.NormaliseDraftBlobPath(blobPath, options.Value.DraftContainerName);
        var errorBlobPath = $"{normalisedPath}{options.Value.DraftErrorBlobSuffix}";
        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var errorBlob = draftContainer.GetBlobClient(errorBlobPath);

        Response<BlobContentInfo>? errorBlobUpload = null;
        try
        {
            var diagnosticPayload = new
            {
                blobPath = normalisedPath,
                schemaName,
                version,
                failedAtUtc = DateTimeOffset.UtcNow,
                errors
            };

            var errorJson = JsonSerializer.Serialize(diagnosticPayload, new JsonSerializerOptions { WriteIndented = true });
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(errorJson));
            errorBlobUpload = await errorBlob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);

            _instrumentation.DraftFailureRecorded(errorBlobPath, errors.Count);
            return Result.Success();
        }
        catch (Exception ex)
        {
            try
            {
                if (errorBlobUpload?.HasValue == true)
                {
                    await errorBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }
            }
            catch
            {
                // Best-effort compensating rollback
            }

            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    private static async Task<string?> TryLoadCompanionAsync(
        BlobContainerClient draftContainer,
        string basePrefix,
        IEnumerable<string> suffixes,
        List<string> consumedBlobs,
        CancellationToken cancellationToken)
    {
        foreach (var suffix in suffixes)
        {
            var companionPath = $"{basePrefix}{suffix}";
            if (consumedBlobs.Contains(companionPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var companionBlob = draftContainer.GetBlobClient(companionPath);
            if (await companionBlob.ExistsAsync(cancellationToken))
            {
                var content = (await companionBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
                consumedBlobs.Add(companionPath);
                return content;
            }
        }

        return null;
    }

    private static async Task<Result<SchemaIdentifierConfig?>> TryLoadDraftIdentifierConfigAsync(
        BlobContainerClient draftContainer,
        string metaBlobPath,
        List<string> consumedBlobs,
        CancellationToken cancellationToken)
    {
        var blob = draftContainer.GetBlobClient(metaBlobPath);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return (SchemaIdentifierConfig?)null;
        }

        var raw = (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        consumedBlobs.Add(metaBlobPath);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return (SchemaIdentifierConfig?)null;
        }

        try
        {
            var node = JsonNode.Parse(raw);
            if (node is not JsonObject obj)
            {
                return Error.Validation(
                    ErrorCodes.SchemaInvalidIdentifierConfig,
                    new { Message = $"Draft metadata file '{metaBlobPath}' must be a JSON object." });
            }

            JsonNode? configNode = null;
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, "identifierConfig", StringComparison.OrdinalIgnoreCase))
                {
                    configNode = kvp.Value;
                    break;
                }
            }

            if (configNode is not null)
            {
                return configNode.Deserialize<SchemaIdentifierConfig>(JsonOptions);
            }

            var hasDirectFields = obj.Any(kvp =>
                string.Equals(kvp.Key, "type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Key, "jsonPointer", StringComparison.OrdinalIgnoreCase));

            if (hasDirectFields)
            {
                return obj.Deserialize<SchemaIdentifierConfig>(JsonOptions);
            }

            return (SchemaIdentifierConfig?)null;
        }
        catch (Exception ex)
        {
            return Error.Validation(
                ErrorCodes.SchemaInvalidIdentifierConfig,
                new { Message = $"Draft metadata file '{metaBlobPath}' is malformed JSON: {ex.Message}" });
        }
    }
}
