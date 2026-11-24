namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionDraftItem(
    string BlobName,
    DateTimeOffset CreatedAtUtc,
    string PayloadJson,
    long SizeBytes);
