using System.Text.Json.Serialization;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Domain.Schema;

/// <summary>
/// Per-schema-version metadata configuration for extracting an establishment identifier
/// (e.g. URN, UKPRN, LAEstab, or any custom scalar property) from a submission payload.
/// Stored in {schemaName}/v{version}.meta.json so that ODCS data contracts
/// remain agnostic of EDC storage partitioning and identifier extraction mechanics.
/// </summary>
public sealed record SchemaIdentifierConfig
{
    public const string NoneType = "None";

    /// <summary>
    /// Identifier type/label (e.g. "URN", "UKPRN", "LAEstab", custom label, or "None" to disable).
    /// </summary>
    public string Type { get; init; } = NoneType;

    /// <summary>
    /// JSON Pointer or dot-notation path to the identifier within the submission payload
    /// (e.g. "/school/urn", "/urn", "/establishment/ukprn", "school.urn").
    /// When null or empty while enabled, falls back to platform defaults.
    /// </summary>
    public string? JsonPointer { get; init; }

    /// <summary>
    /// Automatically derived from the JSON Schema's 'required' declaration along <see cref="JsonPointer"/>
    /// (falling back to ValidationOptions.RequireIdentifierOnFinalise when schema metadata is unavailable).
    /// </summary>
    public bool? Required { get; init; }

    [JsonIgnore]
    public bool IsDisabled => string.Equals(Type?.Trim(), NoneType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Validates structural invariants of this identifier configuration (type label and pointer syntax).
    /// </summary>
    public Result ValidateDefinition()
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            return Error.Validation(
                ErrorCodes.SchemaInvalidIdentifierConfig,
                new { Message = DomainMessages.IdentifierTypeEmpty },
                [new ValidationError("identifierConfig.type", DomainMessages.IdentifierTypeShortEmpty)]);
        }

        var trimmedType = Type.Trim();

        if (IsDisabled)
        {
            if (!string.IsNullOrWhiteSpace(JsonPointer))
            {
                return Error.Validation(
                    ErrorCodes.SchemaInvalidIdentifierConfig,
                    new { Message = DomainMessages.IdentifierPointerForbiddenWhenNone },
                    [new ValidationError("identifierConfig.jsonPointer", DomainMessages.IdentifierPointerShortForbiddenWhenNone)]);
            }

            return Result.Success();
        }

        if (trimmedType.Contains('/') || trimmedType.Contains('\\') || trimmedType.Contains("..") || trimmedType.Any(char.IsWhiteSpace))
        {
            var invalidTypeMsg = DomainMessages.IdentifierTypeInvalidChars(trimmedType);
            return Error.Validation(
                ErrorCodes.SchemaInvalidIdentifierConfig,
                new { Message = invalidTypeMsg },
                [new ValidationError("identifierConfig.type", invalidTypeMsg)]);
        }

        if (!string.IsNullOrWhiteSpace(JsonPointer) && !TryParsePointerSegments(JsonPointer, out _))
        {
            return Error.Validation(
                ErrorCodes.SchemaInvalidIdentifierConfig,
                new { Message = DomainMessages.IdentifierPointerInvalid(JsonPointer) },
                [new ValidationError("identifierConfig.jsonPointer", DomainMessages.IdentifierPointerInvalidWithExamples(JsonPointer))]);
        }

        return Result.Success();
    }

    public SchemaIdentifierConfig Normalise(string? defaultType = null)
    {
        var fallbackType = string.IsNullOrWhiteSpace(defaultType) ? NoneType : defaultType.Trim();
        var trimmedType = string.IsNullOrWhiteSpace(Type) ? fallbackType : Type.Trim();
        if (string.Equals(trimmedType, NoneType, StringComparison.OrdinalIgnoreCase))
        {
            return new SchemaIdentifierConfig
            {
                Type = NoneType,
                JsonPointer = null,
                Required = false
            };
        }

        if (string.Equals(trimmedType, fallbackType, StringComparison.OrdinalIgnoreCase))
        {
            trimmedType = fallbackType;
        }

        var pointer = string.IsNullOrWhiteSpace(JsonPointer) ? null : JsonPointer.Trim();
        if (pointer is not null && !pointer.StartsWith('/') && !pointer.StartsWith("$.", StringComparison.Ordinal))
        {
            pointer = "/" + pointer.Replace('.', '/').TrimStart('/');
        }
        else if (pointer is not null && pointer.StartsWith("$.", StringComparison.Ordinal))
        {
            pointer = "/" + pointer[2..].Replace('.', '/').TrimStart('/');
        }

        return this with
        {
            Type = trimmedType,
            JsonPointer = pointer
        };
    }

    /// <summary>
    /// Parses a JSON Pointer ("/school/urn") or dot-notation path ("school.urn" / "$.school.urn")
    /// into property segments, rejecting empty segments, array wildcards, or malformed syntax.
    /// </summary>
    public static bool TryParsePointerSegments(string? rawPath, out IReadOnlyList<string> segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        var trimmed = rawPath.Trim();
        if (trimmed.Contains('[') || trimmed.Contains(']') || trimmed.Contains('*') || trimmed.Contains('\\'))
        {
            return false;
        }

        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
        {
            var body = trimmed[2..];
            if (string.IsNullOrWhiteSpace(body) || body.StartsWith('.') || body.EndsWith('.') || body.Contains("..", StringComparison.Ordinal))
            {
                return false;
            }

            var dotParts = body.Split('.', StringSplitOptions.None);
            if (dotParts.Any(string.IsNullOrWhiteSpace))
            {
                return false;
            }

            segments = dotParts.Select(p => p.Trim()).ToArray();
            return segments.Count > 0;
        }

        if (trimmed.StartsWith('/'))
        {
            var body = trimmed[1..];
            if (string.IsNullOrEmpty(body) || body.EndsWith('/') || body.Contains("//", StringComparison.Ordinal))
            {
                return false;
            }

            var pointerParts = body.Split('/', StringSplitOptions.None);
            if (pointerParts.Any(string.IsNullOrWhiteSpace))
            {
                return false;
            }

            segments = pointerParts
                .Select(p => p.Replace("~1", "/").Replace("~0", "~").Trim())
                .ToArray();
            return segments.Count > 0 && segments.All(s => !string.IsNullOrWhiteSpace(s));
        }

        // Plain dot-notation ("school.urn" or "urn")
        if (trimmed.StartsWith('.') || trimmed.EndsWith('.') || trimmed.Contains("..", StringComparison.Ordinal) || trimmed.Contains('/'))
        {
            return false;
        }

        var plainParts = trimmed.Split('.', StringSplitOptions.None);
        if (plainParts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        segments = plainParts.Select(p => p.Trim()).ToArray();
        return segments.Count > 0;
    }

    public static string ToJsonPointer(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return "/";
        }

        return "/" + string.Join('/', segments.Select(s => s.Replace("~", "~0").Replace("/", "~1")));
    }
}
