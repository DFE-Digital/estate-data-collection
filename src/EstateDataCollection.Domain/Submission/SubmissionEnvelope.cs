using System.Security.Cryptography;
using System.Text;

namespace EstateDataCollection.Domain.Submission;

public class SubmissionEnvelope
{
    public required Guid SubmissionId { get; init; }
    public required int Urn { get; init; }
    public required string SchemaName { get; init; }
    public required int SchemaVersion { get; init; }
    public required string RawPayloadJson { get; init; }
    public required string PayloadSha256Hash { get; init; }
    public required DateTimeOffset SubmittedAtUtc { get; init; }
    
    #region Domain Factories

    public static SubmissionEnvelope Create(
        int urn,
        string schemaName,
        int schemaVersion,
        string payloadJson,
        DateTimeOffset submittedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(urn);
        ArgumentException.ThrowIfNullOrEmpty(schemaName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentException.ThrowIfNullOrEmpty(payloadJson);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(submittedAtUtc, DateTimeOffset.MinValue);
        
        return new SubmissionEnvelope
        {
            SubmissionId = Guid.CreateVersion7(),
            Urn = urn,
            SchemaName = schemaName,
            SchemaVersion = schemaVersion,
            RawPayloadJson = payloadJson,
            PayloadSha256Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))),
            SubmittedAtUtc = submittedAtUtc
        };
    }
    
    #endregion
}