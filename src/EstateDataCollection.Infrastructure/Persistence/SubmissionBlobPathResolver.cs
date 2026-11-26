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

    public static string ResolveAuditPrefix(string auditBlobPrefixFormat, Guid submissionId)
    {
        var prefix = auditBlobPrefixFormat
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

    public static string BuildAuditFileName(DateTimeOffset timestampUtc, Guid eventId)
    {
        var formattedTs = timestampUtc.ToUniversalTime().ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        return $"{formattedTs}_{eventId:N}.json";
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
        Guid submissionId,
        string defaultIdentifierType = SchemaIdentifierConfig.DefaultIdentifierType)
    {
        var safeSchemaName = SanitiseBlobPathSegment(schemaName);
        var format = processedBlobNameFormat;
        var effectiveValue = string.IsNullOrWhiteSpace(identifierValue) ? null : identifierValue.Trim();

        if (effectiveValue is null)
        {
            format = format
                .Replace("{identifierType}={establishmentIdentifier}/", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/{identifierType}={establishmentIdentifier}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{identifierType}={identifierValue}/", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/{identifierType}={identifierValue}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("urn={establishmentIdentifier}/", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/urn={establishmentIdentifier}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("urn={identifierValue}/", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/urn={identifierValue}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("urn={urn}/", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/urn={urn}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .TrimStart('/');

            return format.FormatWith(new
            {
                identifierType = string.Empty,
                establishmentIdentifier = string.Empty,
                identifierValue = string.Empty,
                urn = string.Empty,
                identifier = string.Empty,
                schemaName = safeSchemaName,
                schemaVersion,
                submissionId
            });
        }

        var safeValue = SanitiseBlobPathSegment(effectiveValue);
        var fallbackType = string.IsNullOrWhiteSpace(defaultIdentifierType)
            ? SchemaIdentifierConfig.DefaultIdentifierType
            : defaultIdentifierType.Trim();
        var effectiveType = string.IsNullOrWhiteSpace(identifierType)
            ? fallbackType
            : identifierType.Trim();

        var safeType = SanitiseBlobPathSegment(effectiveType.ToLowerInvariant());

        if (!format.Contains("{identifierType}", StringComparison.OrdinalIgnoreCase))
        {
            format = format
                .Replace("urn={establishmentIdentifier}", $"{safeType}={safeValue}", StringComparison.OrdinalIgnoreCase)
                .Replace("urn={identifierValue}", $"{safeType}={safeValue}", StringComparison.OrdinalIgnoreCase)
                .Replace("urn={urn}", $"{safeType}={safeValue}", StringComparison.OrdinalIgnoreCase);
        }

        return format.FormatWith(new
        {
            identifierType = safeType,
            establishmentIdentifier = safeValue,
            identifierValue = safeValue,
            urn = safeValue,
            identifier = safeValue,
            schemaName = safeSchemaName,
            schemaVersion,
            submissionId
        });
    }

    public static string ResolveProcessedBundlePrefix(
        string processedBlobNameFormat,
        string? identifierType,
        string? identifierValue,
        string schemaName,
        int schemaVersion,
        Guid submissionId,
        string defaultIdentifierType = SchemaIdentifierConfig.DefaultIdentifierType)
    {
        var resolvedBlobName = ResolveProcessedBlobName(
            processedBlobNameFormat,
            identifierType,
            identifierValue,
            schemaName,
            schemaVersion,
            submissionId,
            defaultIdentifierType);

        var lastSlash = resolvedBlobName.LastIndexOf('/');
        var directory = lastSlash >= 0 ? resolvedBlobName[..(lastSlash + 1)] : string.Empty;

        var submissionSegment = $"{submissionId}/";
        if (directory.EndsWith(submissionSegment, StringComparison.OrdinalIgnoreCase) ||
            directory.Contains($"/{submissionSegment}", StringComparison.OrdinalIgnoreCase))
        {
            return directory;
        }

        return $"{directory}{submissionSegment}";
    }

    public static string BuildProcessedPayloadBlobName(string bundlePrefix)
    {
        var normalised = NormalisePrefix(bundlePrefix);
        return $"{normalised}payload.json";
    }

    public static string BuildProcessedSessionBlobName(string bundlePrefix)
    {
        var normalised = NormalisePrefix(bundlePrefix);
        return $"{normalised}session.json";
    }

    public static string BuildProcessedAuditBlobName(string bundlePrefix, string auditFileName)
    {
        var normalised = NormalisePrefix(bundlePrefix);
        return $"{normalised}audit/{auditFileName.TrimStart('/')}";
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

    private static string NormalisePrefix(string prefix)
    {
        var trimmed = prefix.Trim().TrimStart('/');
        return trimmed.EndsWith('/') ? trimmed : $"{trimmed}/";
    }
}
