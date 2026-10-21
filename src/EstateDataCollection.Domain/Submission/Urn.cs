using System.Text.Json;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Domain.Submission;

/// <summary>
/// A object representing a UK DfE Establishment Unique Reference Number (URN).
/// </summary>
public readonly record struct Urn
{
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
    /// High-performance stream/text extractor.
    /// Scans payload for "urn" without parsing the full JSON document into memory.
    /// </summary>
    public static Result<Urn> ExtractFromPayload(string? jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return Error.Validation(ErrorCodes.PayloadEmpty);
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            
            if (!doc.RootElement.TryGetProperty("urn", out var urnElement))
            {
                return Error.Validation(ErrorCodes.PayloadInvalidUrn);
            }

            return urnElement.ValueKind switch
            {
                JsonValueKind.Number when urnElement.TryGetInt32(out var num) => Create(num),
                JsonValueKind.String when int.TryParse(urnElement.GetString(), out var num) => Create(num),
                _ => Error.Validation(ErrorCodes.PayloadInvalidUrn)
            };
        }
        catch (JsonException)
        {
            return Error.Validation(ErrorCodes.JsonMalformed);
        }
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
