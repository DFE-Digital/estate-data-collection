using System.Text.Json;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Domain.Submission;

/// <summary>
/// An object representing a UK DfE Establishment Unique Reference Number (URN).
/// </summary>
public readonly record struct Urn
{
    private static readonly IReadOnlyList<string> DefaultUrnPaths = ["school.urn", "urn"];

    public int Value { get; }

    private Urn(int value)
    {
        Value = value;
    }
    
    public static Result<Urn> Create(int value)
    {
        if (value <= 0)
        {
            return Error.Validation(ErrorCodes.PayloadInvalidUrn);
        }

        return new Urn(value);
    }
    
    /// <summary>
    /// Parses a raw string or integer representation (e.g. "100234" or 100234).
    /// </summary>
    public static Result<Urn> TryParse(object? raw)
    {
        if (raw is null)
        {
            return Error.Validation(ErrorCodes.PayloadInvalidUrn);
        }

        if (raw is int intVal)
        {
            return Create(intVal);
        }

        if (int.TryParse(raw.ToString(), out var parsedInt))
        {
            return Create(parsedInt);
        }

        return Error.Validation(ErrorCodes.PayloadInvalidUrn);
    }
    
    /// <summary>
    /// Extracts URN from a JSON payload using configurable JSON Pointer ("/school/urn") or dot-separated paths ("school.urn", "urn").
    /// </summary>
    public static Result<Urn> ExtractFromPayload(string? jsonPayload, IReadOnlyList<string>? jsonPaths = null)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return Error.Validation(ErrorCodes.PayloadEmpty);
        }

        var pathsToSearch = jsonPaths is { Count: > 0 } ? jsonPaths : DefaultUrnPaths;

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);

            foreach (var rawPath in pathsToSearch)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                if (TryNavigatePath(doc.RootElement, rawPath, out var urnElement))
                {
                    return urnElement.ValueKind switch
                    {
                        JsonValueKind.Number when urnElement.TryGetInt32(out var num) => Create(num),
                        JsonValueKind.String when int.TryParse(urnElement.GetString(), out var num) => Create(num),
                        _ => Error.Validation(ErrorCodes.PayloadInvalidUrn)
                    };
                }
            }

            return Error.Validation(ErrorCodes.PayloadInvalidUrn);
        }
        catch (JsonException)
        {
            return Error.Validation(ErrorCodes.JsonMalformed);
        }
    }

    private static bool TryNavigatePath(JsonElement root, string rawPath, out JsonElement element)
    {
        element = root;
        var normalized = rawPath.Trim();
        if (normalized.StartsWith("$.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        else if (normalized.StartsWith('/'))
        {
            normalized = normalized[1..];
        }

        normalized = normalized.Replace('/', '.');

        var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var current = root;
        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!current.TryGetProperty(segment, out var next))
            {
                // Case-insensitive fallback
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

    public static implicit operator int(Urn urn)
    {
        return urn.Value;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
