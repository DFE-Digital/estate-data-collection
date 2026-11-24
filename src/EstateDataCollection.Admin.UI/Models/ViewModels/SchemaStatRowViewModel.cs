using System.Globalization;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;
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