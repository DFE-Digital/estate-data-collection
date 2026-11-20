namespace EstateDataCollection.Domain.Submission;

public readonly record struct IdentifierExtractionOutcome(
    IdentifierExtractionStatus Status,
    SubmissionIdentifier? Identifier = null,
    string? MatchedPath = null,
    string? ErrorMessage = null);