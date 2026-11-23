using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class GetSubmissionStatisticsResultBuilder
{
    private int _totalSubmissions;
    private int _draftSubmissions;
    private int _draftWithPayloadSubmissions;
    private int _emptyDraftSubmissions;
    private int _finalisedSubmissions;
    private int _submissionsLast24Hours;
    private int _submissionsLast7Days;
    private int _totalDraftChunks;
    private int _totalAuditEvents;
    private int _finalisationRatePercent;
    private readonly List<SchemaSubmissionStatistic> _schemaStatistics = [];
    private readonly List<SubmissionSummary> _recentSubmissions = [];
    private readonly List<string> _availableSchemaNames = [];
    private string? _filteredSchemaName;

    public GetSubmissionStatisticsResultBuilder WithSubmissionCounts(
        int total,
        int draft,
        int draftWithPayload,
        int emptyDraft,
        int finalised,
        int finalisationRatePercent = 0)
    {
        _totalSubmissions = total;
        _draftSubmissions = draft;
        _draftWithPayloadSubmissions = draftWithPayload;
        _emptyDraftSubmissions = emptyDraft;
        _finalisedSubmissions = finalised;
        _finalisationRatePercent = finalisationRatePercent;
        return this;
    }

    public GetSubmissionStatisticsResultBuilder WithRecentActivity(int last24Hours, int last7Days)
    {
        _submissionsLast24Hours = last24Hours;
        _submissionsLast7Days = last7Days;
        return this;
    }

    public GetSubmissionStatisticsResultBuilder WithArtifactTotals(int totalDraftChunks, int totalAuditEvents)
    {
        _totalDraftChunks = totalDraftChunks;
        _totalAuditEvents = totalAuditEvents;
        return this;
    }

    public GetSubmissionStatisticsResultBuilder WithSchemaStatistic(SchemaSubmissionStatistic statistic)
    {
        _schemaStatistics.Add(statistic);
        return this;
    }

    public GetSubmissionStatisticsResultBuilder WithSchemaStatistic(
        string schemaName,
        int version = 1,
        string state = "Active",
        int totalSubmissions = 0,
        int draftSubmissions = 0,
        int draftWithPayloadSubmissions = 0,
        int emptyDraftSubmissions = 0,
        int finalisedSubmissions = 0,
        int totalDraftChunks = 0,
        int totalAuditEvents = 0,
        int finalisationRatePercent = 0,
        DateTimeOffset? lastActivityAtUtc = null)
    {
        _schemaStatistics.Add(new SchemaSubmissionStatistic(
            schemaName,
            version,
            state,
            totalSubmissions,
            draftSubmissions,
            draftWithPayloadSubmissions,
            emptyDraftSubmissions,
            finalisedSubmissions,
            totalDraftChunks,
            totalAuditEvents,
            finalisationRatePercent,
            lastActivityAtUtc));
        return this;
    }

    public GetSubmissionStatisticsResultBuilder WithRecentSubmission(SubmissionSummary summary)
    {
        _recentSubmissions.Add(summary);
        return this;
    }

    public GetSubmissionStatisticsResultBuilder WithRecentSubmission(Action<SubmissionSummaryBuilder> configure)
    {
        var builder = new SubmissionSummaryBuilder();
        configure(builder);
        _recentSubmissions.Add(builder.Build());
        return this;
    }

    public GetSubmissionStatisticsResultBuilder WithAvailableSchemas(params string[] schemaNames)
    {
        _availableSchemaNames.AddRange(schemaNames);
        return this;
    }

    public GetSubmissionStatisticsResultBuilder FilteredBySchema(string? schemaName)
    {
        _filteredSchemaName = schemaName;
        return this;
    }

    public GetSubmissionStatisticsResult Build()
    {
        return new GetSubmissionStatisticsResult(
            TotalSubmissions: _totalSubmissions,
            DraftSubmissions: _draftSubmissions,
            DraftWithPayloadSubmissions: _draftWithPayloadSubmissions,
            EmptyDraftSubmissions: _emptyDraftSubmissions,
            FinalisedSubmissions: _finalisedSubmissions,
            SubmissionsLast24Hours: _submissionsLast24Hours,
            SubmissionsLast7Days: _submissionsLast7Days,
            TotalDraftChunks: _totalDraftChunks,
            TotalAuditEvents: _totalAuditEvents,
            FinalisationRatePercent: _finalisationRatePercent,
            SchemaStatistics: _schemaStatistics.ToArray(),
            RecentSubmissions: _recentSubmissions.ToArray(),
            AvailableSchemaNames: _availableSchemaNames.ToArray(),
            FilteredSchemaName: _filteredSchemaName);
    }
}
