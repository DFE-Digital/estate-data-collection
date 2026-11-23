namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;

public sealed record SchemaSubmissionStatistic(
    string SchemaName,
    int Version,
    string SchemaState,
    int TotalSubmissions,
    int DraftSubmissions,
    int DraftWithPayloadSubmissions,
    int EmptyDraftSubmissions,
    int FinalisedSubmissions,
    int TotalDraftChunks,
    int TotalAuditEvents,
    int FinalisationRatePercent,
    DateTimeOffset? LastActivityAtUtc);