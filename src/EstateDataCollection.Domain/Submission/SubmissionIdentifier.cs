using System.Globalization;
using System.Text.Json;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Domain.Submission;

/// <summary>
/// Represents an extracted school / establishment identifier (URN, UKPRN, LAEstab, or custom scalar key)
/// resolved from a submission payload using the schema's configured pointer.
/// </summary>
public readonly record struct SubmissionIdentifier
{
    private static readonly IReadOnlyList<string> DefaultIdentifierPaths = ["school.urn", "urn"];

    public string Type { get; }
    public string Value { get; }

    public int? AsPositiveInt
    {
        get
        {
            return int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : null;
        }
    }

    private SubmissionIdentifier(string type, string value)
    {
        Type = type;
        Value = value;
    }

    public static Result<SubmissionIdentifier> Create(string? type, string? rawValue)
    {
        var effectiveType = string.IsNullOrWhiteSpace(type)
            ? SchemaIdentifierConfig.DefaultUrnType
            : type.Trim();

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return CreateValidationError(effectiveType, $"Identifier '{effectiveType}' cannot be empty.");
        }

        var trimmed = rawValue.Trim();
        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains("..", StringComparison.Ordinal))
        {
            return CreateValidationError(
                effectiveType,
                $"Identifier '{effectiveType}' value '{trimmed}' contains characters not permitted for storage partitioning ('/', '\\', or '..').");
        }

        if (string.Equals(effectiveType, SchemaIdentifierConfig.DefaultUrnType, StringComparison.OrdinalIgnoreCase) &&
            decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var numVal) &&
            (numVal <= 0 || decimal.Truncate(numVal) != numVal))
        {
            return Error.Validation(
                ErrorCodes.PayloadInvalidUrn,
                "The 'urn' identifier must be a positive integer or valid identifier string.");
        }

        return new SubmissionIdentifier(effectiveType, trimmed);
    }

    /// <summary>
    /// Extracts an identifier from a JSON payload along the configured path(s), distinguishing between
    /// "NotPresent" (absent or explicit JSON null) and "Invalid" (wrong type, empty, non-positive URN, unsafe path chars).
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
            ? SchemaIdentifierConfig.DefaultUrnType
            : identifierType.Trim();

        var pathsToSearch = jsonPaths is { Count: > 0 } ? jsonPaths : DefaultIdentifierPaths;

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
                    var isUrn = string.Equals(effectiveType, SchemaIdentifierConfig.DefaultUrnType, StringComparison.OrdinalIgnoreCase);
                    if (isUrn)
                    {
                        if (!element.TryGetInt32(out var urnInt) || urnInt <= 0)
                        {
                            return new IdentifierExtractionOutcome(
                                IdentifierExtractionStatus.Invalid,
                                MatchedPath: pointerPath,
                                ErrorMessage: $"Identifier '{effectiveType}' at '{pointerPath}' must be a positive integer.");
                        }

                        return new IdentifierExtractionOutcome(
                            IdentifierExtractionStatus.Found,
                            new SubmissionIdentifier(effectiveType, urnInt.ToString(CultureInfo.InvariantCulture)),
                            pointerPath);
                    }

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
                            ErrorMessage: $"Identifier '{effectiveType}' at '{pointerPath}' cannot be empty.");
                    }

                    var createStr = Create(effectiveType, strVal);
                    return createStr.IsSuccess
                        ? new IdentifierExtractionOutcome(IdentifierExtractionStatus.Found, createStr.Value, pointerPath)
                        : new IdentifierExtractionOutcome(IdentifierExtractionStatus.Invalid, MatchedPath: pointerPath, ErrorMessage: createStr.Error.Description);
                }

                return new IdentifierExtractionOutcome(
                    IdentifierExtractionStatus.Invalid,
                    MatchedPath: pointerPath,
                    ErrorMessage: $"Identifier '{effectiveType}' at '{pointerPath}' must be a scalar string or number, not {element.ValueKind}.");
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

    private static Error CreateValidationError(string effectiveType, string message)
    {
        return string.Equals(effectiveType, SchemaIdentifierConfig.DefaultUrnType, StringComparison.OrdinalIgnoreCase)
            ? Error.Validation(ErrorCodes.PayloadInvalidUrn, message)
            : Error.Validation(ErrorCodes.PayloadInvalidIdentifier, new { Message = message });
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
