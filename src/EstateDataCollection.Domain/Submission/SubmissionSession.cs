using System.Text.Json.Serialization;

namespace EstateDataCollection.Domain.Submission;

public sealed class SubmissionSession
{
    public required Guid SubmissionId { get; init; }
    public required string SchemaName { get; init; }
    public required int SchemaVersion { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonInclude]
    public string? IdentifierType { get; private set; }

    [JsonInclude]
    public string? IdentifierValue { get; private set; }

    [JsonInclude]
    public DateTimeOffset? FinalisedAtUtc { get; private set; }

    public bool IsFinalised
    {
        get { return FinalisedAtUtc.HasValue; }
    }

    public static SubmissionSession Create(string schemaName, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);

        return new SubmissionSession
        {
            SubmissionId = Guid.CreateVersion7(),
            SchemaName = schemaName,
            SchemaVersion = schemaVersion
        };
    }

    public void AssignIdentifier(string? identifierType, string? identifierValue)
    {
        IdentifierType = string.IsNullOrWhiteSpace(identifierType) ? null : identifierType.Trim();
        IdentifierValue = string.IsNullOrWhiteSpace(identifierValue) ? null : identifierValue.Trim();
    }

    public void Finalise()
    {
        FinalisedAtUtc = DateTimeOffset.UtcNow;
    }
}
