using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public sealed record FinaliseSubmissionResult(
    Guid SubmissionId,
    string SchemaName,
    int SchemaVersion,
    string Status,
    string ProcessedBlobUri,
    DateTimeOffset FinalisedAtUtc,
    string? Identifier = null,
    string? IdentifierType = null,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null);
