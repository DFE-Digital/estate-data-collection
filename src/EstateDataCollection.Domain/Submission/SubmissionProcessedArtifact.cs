namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionProcessedArtifact(
    string BlobName,
    string BlobUri,
    string PayloadJson,
    string? Sha256Hash,
    long SizeBytes,
    DateTimeOffset? FinalisedAtUtc,
    string? Status);
