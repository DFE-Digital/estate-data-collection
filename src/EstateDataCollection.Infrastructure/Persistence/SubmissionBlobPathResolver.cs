using System.Globalization;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Infrastructure.Persistence;

internal static class SubmissionBlobPathResolver
{
    public static string ResolveDraftPrefix(string draftBlobPrefixFormat, Guid submissionId)
    {
        var prefix = draftBlobPrefixFormat
            .FormatWith(new { submissionId })
            .TrimStart('/');

        return prefix.EndsWith('/') ? prefix : $"{prefix}/";
    }

    public static string BuildTimestampedDraftBlobName(
        string draftBlobPrefixFormat,
        Guid submissionId,
        DateTimeOffset timestampUtc,
        Guid chunkId)
    {
        var prefix = ResolveDraftPrefix(draftBlobPrefixFormat, submissionId);
        var formattedTs = timestampUtc.ToUniversalTime().ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        return $"{prefix}{formattedTs}_{chunkId:N}.json";
    }

    public static DateTimeOffset? TryReadTimestampFromMetadata(
        IDictionary<string, string>? metadata,
        string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    public static string ResolveProcessedBlobName(
        string processedBlobNameFormat,
        string? identifierType,
        string? identifierValue,
        string schemaName,
        int schemaVersion,
        Guid submissionId)
    {
        var safeSchemaName = SanitiseBlobPathSegment(schemaName);
        var format = processedBlobNameFormat;
        var effectiveValue = string.IsNullOrWhiteSpace(identifierValue) ? null : identifierValue.Trim();

        if (effectiveValue is null)
        {
            format = format
                .Replace("urn={urn}/", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/urn={urn}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .TrimStart('/');

            return format.FormatWith(new
            {
                urn = string.Empty,
                identifier = string.Empty,
                schemaName = safeSchemaName,
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
            schemaName = safeSchemaName,
            schemaVersion,
            submissionId
        });
    }

    public static string SanitiseBlobPathSegment(string value)
    {
        var sanitised = value.Trim()
            .Replace('/', '-')
            .Replace('\\', '-')
            .Replace('%', '_')
            .Replace("..", "_", StringComparison.Ordinal);

        return sanitised is "." or "" ? "_" : sanitised;
    }
}
