using System.Globalization;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaStatRowViewModel(
    string SchemaName,
    int Version,
    string SchemaDisplayName,
    LifecycleTagViewModel SchemaStateTag,
    string SchemaDetailUrl,
    string AllSubmissionsUrl,
    string DraftSubmissionsUrl,
    string FinalisedSubmissionsUrl,
    int TotalSubmissions,
    int DraftSubmissions,
    int DraftWithPayloadSubmissions,
    int EmptyDraftSubmissions,
    int FinalisedSubmissions,
    int TotalDraftChunks,
    int TotalAuditEvents,
    string FinalisationRateDisplay,
    string LastActivityDisplay)
{
    public bool HasSubmissions
    {
        get { return TotalSubmissions > 0; }
    }

    public static SchemaStatRowViewModel From(SchemaSubmissionStatistic stat)
    {
        return new SchemaStatRowViewModel(
            SchemaName: stat.SchemaName,
            Version: stat.Version,
            SchemaDisplayName: $"{stat.SchemaName} v{stat.Version}",
            SchemaStateTag: LifecycleTagViewModel.From(stat.SchemaState),
            SchemaDetailUrl: AppRoutes.AdminUi.SchemaDetail(stat.SchemaName, stat.Version),
            AllSubmissionsUrl: AppRoutes.AdminUi.SubmissionsFiltered(stat.SchemaName, stat.Version),
            DraftSubmissionsUrl: AppRoutes.AdminUi.SubmissionsFiltered(stat.SchemaName, stat.Version, status: "Draft"),
            FinalisedSubmissionsUrl: AppRoutes.AdminUi.SubmissionsFiltered(stat.SchemaName, stat.Version, status: "Finalised"),
            TotalSubmissions: stat.TotalSubmissions,
            DraftSubmissions: stat.DraftSubmissions,
            DraftWithPayloadSubmissions: stat.DraftWithPayloadSubmissions,
            EmptyDraftSubmissions: stat.EmptyDraftSubmissions,
            FinalisedSubmissions: stat.FinalisedSubmissions,
            TotalDraftChunks: stat.TotalDraftChunks,
            TotalAuditEvents: stat.TotalAuditEvents,
            FinalisationRateDisplay: stat.TotalSubmissions > 0 ? $"{stat.FinalisationRatePercent}%" : "—",
            LastActivityDisplay: stat.LastActivityAtUtc.HasValue
                ? stat.LastActivityAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                : "No submissions yet");
    }
}

public sealed record SelectSchemaFilterOptionViewModel(
    string Value,
    string Label,
    bool IsSelected);

public sealed record StatisticsViewModel(
    AdminPageMeta Page,
    GetSubmissionStatisticsResult? Statistics,
    Error? PageError = null)
{
    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(PageError); }
    }

    public bool HasStatistics
    {
        get { return Statistics is not null; }
    }

    public int TotalSubmissions
    {
        get { return Statistics?.TotalSubmissions ?? 0; }
    }

    public int DraftSubmissions
    {
        get { return Statistics?.DraftSubmissions ?? 0; }
    }

    public int DraftWithPayloadSubmissions
    {
        get { return Statistics?.DraftWithPayloadSubmissions ?? 0; }
    }

    public int EmptyDraftSubmissions
    {
        get { return Statistics?.EmptyDraftSubmissions ?? 0; }
    }

    public int FinalisedSubmissions
    {
        get { return Statistics?.FinalisedSubmissions ?? 0; }
    }

    public int SubmissionsLast24Hours
    {
        get { return Statistics?.SubmissionsLast24Hours ?? 0; }
    }

    public int SubmissionsLast7Days
    {
        get { return Statistics?.SubmissionsLast7Days ?? 0; }
    }

    public int TotalDraftChunks
    {
        get { return Statistics?.TotalDraftChunks ?? 0; }
    }

    public int TotalAuditEvents
    {
        get { return Statistics?.TotalAuditEvents ?? 0; }
    }

    public string FinalisationRateDisplay
    {
        get { return TotalSubmissions > 0 ? $"{Statistics!.FinalisationRatePercent}%" : "—"; }
    }

    public string FilteredSchemaName
    {
        get { return Statistics?.FilteredSchemaName ?? string.Empty; }
    }

    public bool HasSchemaFilter
    {
        get { return !string.IsNullOrWhiteSpace(FilteredSchemaName); }
    }

    public IReadOnlyList<SelectSchemaFilterOptionViewModel> SchemaOptions
    {
        get
        {
            return _schemaOptions ??= (Statistics?.AvailableSchemaNames ?? [])
                .Select(name => new SelectSchemaFilterOptionViewModel(
                    Value: name,
                    Label: name,
                    IsSelected: string.Equals(name, FilteredSchemaName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    private IReadOnlyList<SelectSchemaFilterOptionViewModel>? _schemaOptions;

    public IReadOnlyList<SchemaStatRowViewModel> SchemaRows
    {
        get
        {
            return _schemaRows ??= (Statistics?.SchemaStatistics ?? [])
                .Select(SchemaStatRowViewModel.From)
                .ToList();
        }
    }

    private IReadOnlyList<SchemaStatRowViewModel>? _schemaRows;

    public bool HasSchemaRows
    {
        get { return SchemaRows.Count > 0; }
    }

    public IReadOnlyList<SubmissionListItemViewModel> RecentSubmissions
    {
        get
        {
            return _recentSubmissions ??= (Statistics?.RecentSubmissions ?? [])
                .Select(SubmissionListItemViewModel.From)
                .ToList();
        }
    }

    private IReadOnlyList<SubmissionListItemViewModel>? _recentSubmissions;

    public bool HasRecentSubmissions
    {
        get { return RecentSubmissions.Count > 0; }
    }
}
