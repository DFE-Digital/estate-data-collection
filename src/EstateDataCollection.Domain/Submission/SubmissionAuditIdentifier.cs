namespace EstateDataCollection.Domain.Submission;

public sealed record SubmissionAuditIdentifier(string? Type, string? Value, int? Urn = null);