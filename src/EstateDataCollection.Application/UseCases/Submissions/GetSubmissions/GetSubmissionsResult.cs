using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;

public sealed record GetSubmissionsResult(
    IReadOnlyList<SubmissionSummary> Submissions,
    IReadOnlyList<string> AvailableSchemaNames,
    string? SchemaName,
    int? SchemaVersion,
    string? Status,
    string? Query);
