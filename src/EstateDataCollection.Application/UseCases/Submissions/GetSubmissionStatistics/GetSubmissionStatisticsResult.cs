using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;

public sealed record GetSubmissionStatisticsResult(
    int TotalSubmissions,
    int DraftSubmissions,
    int DraftWithPayloadSubmissions,
    int EmptyDraftSubmissions,
    int FinalisedSubmissions,
    int SubmissionsLast24Hours,
    int SubmissionsLast7Days,
    int TotalDraftChunks,
    int TotalAuditEvents,
    int FinalisationRatePercent,
    IReadOnlyList<SchemaSubmissionStatistic> SchemaStatistics,
    IReadOnlyList<SubmissionSummary> RecentSubmissions,
    IReadOnlyList<string> AvailableSchemaNames,
    string? FilteredSchemaName = null);
