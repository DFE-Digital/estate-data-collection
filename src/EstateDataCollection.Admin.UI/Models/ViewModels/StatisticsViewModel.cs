using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record StatisticsViewModel(
    AdminPageMeta Page,
    GetSubmissionStatisticsResult? Statistics,
    Error? PageError = null)
{
    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(PageError);

    public bool HasStatistics => Statistics is not null;

    public int TotalSubmissions => Statistics?.TotalSubmissions ?? 0;

    public int DraftSubmissions => Statistics?.DraftSubmissions ?? 0;

    public int DraftWithPayloadSubmissions => Statistics?.DraftWithPayloadSubmissions ?? 0;

    public int EmptyDraftSubmissions => Statistics?.EmptyDraftSubmissions ?? 0;

    public int FinalisedSubmissions => Statistics?.FinalisedSubmissions ?? 0;

    public int SubmissionsLast24Hours => Statistics?.SubmissionsLast24Hours ?? 0;

    public int SubmissionsLast7Days => Statistics?.SubmissionsLast7Days ?? 0;

    public int TotalDraftChunks => Statistics?.TotalDraftChunks ?? 0;

    public int TotalAuditEvents => Statistics?.TotalAuditEvents ?? 0;

    public string FinalisationRateDisplay => TotalSubmissions > 0 ? $"{Statistics!.FinalisationRatePercent}%" : "—";

    public string FilteredSchemaName => Statistics?.FilteredSchemaName ?? string.Empty;

    public bool HasSchemaFilter => !string.IsNullOrWhiteSpace(FilteredSchemaName);

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

    public bool HasSchemaRows => SchemaRows.Count > 0;

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

    public bool HasRecentSubmissions => RecentSubmissions.Count > 0;
}
