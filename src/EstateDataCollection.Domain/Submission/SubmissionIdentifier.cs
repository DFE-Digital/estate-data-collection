using System.Globalization;
using System.Text.Json;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Domain.Submission;

/// <summary>
/// Represents an extracted establishment identifier (e.g. URN, UKPRN, LAEstab, or custom scalar key)
/// resolved from a submission payload using the schema's configured pointer.
/// </summary>
public readonly record struct SubmissionIdentifier
{
    public string Type { get; }
    public string Value { get; }

    public int? AsPositiveInt =>
        int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private SubmissionIdentifier(string type, string value)
    {
        Type = type;
        Value = value;
    }

    public static Result<SubmissionIdentifier> Create(string? type, string? rawValue)
    {
        var effectiveType = string.IsNullOrWhiteSpace(type)
            ? SchemaIdentifierConfig.DefaultIdentifierType
            : type.Trim();

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return CreateValidationError(DomainMessages.IdentifierCannotBeEmpty(effectiveType));
        }

        var trimmed = rawValue.Trim();
        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains("..", StringComparison.Ordinal))
        {
            return CreateValidationError(DomainMessages.IdentifierUnsafePartitionChars(effectiveType, trimmed));
        }

        return new SubmissionIdentifier(effectiveType, trimmed);
    }

    /// <summary>
    /// Extracts an identifier from a JSON payload along the configured path(s), distinguishing between
    /// "NotPresent" (absent or explicit JSON null) and "Invalid" (wrong type, empty, unsafe path chars).
    /// Format or range rules belong in the schema contract rather than identifier type labels.
    /// </summary>
    public static Result<IdentifierExtractionOutcome> InspectPayload(
        string? jsonPayload,
        IReadOnlyList<string>? jsonPaths = null,
        string? identifierType = null)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return Error.Validation(ErrorCodes.PayloadEmpty);
        }

        var effectiveType = string.IsNullOrWhiteSpace(identifierType)
            ? SchemaIdentifierConfig.DefaultIdentifierType
            : identifierType.Trim();

        var pathsToSearch = jsonPaths ?? [];

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            var firstNormalizedPath = TryFormatPointer(pathsToSearch.FirstOrDefault());

            foreach (var rawPath in pathsToSearch)
            {
                if (!SchemaIdentifierConfig.TryParsePointerSegments(rawPath, out var segments))
                {
                    continue;
                }

                var pointerPath = SchemaIdentifierConfig.ToJsonPointer(segments);
                if (!TryNavigateSegments(doc.RootElement, segments, out var element))
                {
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                {
                    return new IdentifierExtractionOutcome(
                        IdentifierExtractionStatus.NotPresent,
                        MatchedPath: pointerPath);
                }

                if (element.ValueKind == JsonValueKind.Number)
                {
                    var numText = element.GetRawText();
                    var createNum = Create(effectiveType, numText);
                    return createNum.IsSuccess
                        ? new IdentifierExtractionOutcome(IdentifierExtractionStatus.Found, createNum.Value, pointerPath)
                        : new IdentifierExtractionOutcome(IdentifierExtractionStatus.Invalid, MatchedPath: pointerPath, ErrorMessage: createNum.Error.Description);
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    var strVal = element.GetString();
                    if (string.IsNullOrWhiteSpace(strVal))
                    {
                        return new IdentifierExtractionOutcome(
                            IdentifierExtractionStatus.Invalid,
                            MatchedPath: pointerPath,
                            ErrorMessage: DomainMessages.IdentifierAtPathCannotBeEmpty(effectiveType, pointerPath));
                    }

                    var createStr = Create(effectiveType, strVal);
                    return createStr.IsSuccess
                        ? new IdentifierExtractionOutcome(IdentifierExtractionStatus.Found, createStr.Value, pointerPath)
                        : new IdentifierExtractionOutcome(IdentifierExtractionStatus.Invalid, MatchedPath: pointerPath, ErrorMessage: createStr.Error.Description);
                }

                return new IdentifierExtractionOutcome(
                    IdentifierExtractionStatus.Invalid,
                    MatchedPath: pointerPath,
                    ErrorMessage: DomainMessages.IdentifierMustBeScalar(effectiveType, pointerPath, element.ValueKind));
            }

            return new IdentifierExtractionOutcome(
                IdentifierExtractionStatus.NotPresent,
                MatchedPath: firstNormalizedPath);
        }
        catch (JsonException)
        {
            return Error.Validation(ErrorCodes.JsonMalformed);
        }
    }

    private static Error CreateValidationError(string message)
    {
        return Error.Validation(ErrorCodes.PayloadInvalidIdentifier, new { Message = message });
    }

    private static string? TryFormatPointer(string? rawPath)
    {
        return SchemaIdentifierConfig.TryParsePointerSegments(rawPath, out var segments)
            ? SchemaIdentifierConfig.ToJsonPointer(segments)
            : rawPath;
    }

    private static bool TryNavigateSegments(JsonElement root, IReadOnlyList<string> segments, out JsonElement element)
    {
        element = root;
        var current = root;

        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!current.TryGetProperty(segment, out var next))
            {
                var matched = false;
                foreach (var prop in current.EnumerateObject())
                {
                    if (string.Equals(prop.Name, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        next = prop.Value;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            current = next;
        }

        element = current;
        return true;
    }

    public override string ToString()
    {
        return Value;
    }
}
