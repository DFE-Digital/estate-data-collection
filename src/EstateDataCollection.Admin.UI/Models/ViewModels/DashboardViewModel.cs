using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record DashboardViewModel(
    AdminPageMeta Page,
    IReadOnlyList<SchemaCatalogueEntry> Schemas,
    string? SuccessBanner = null,
    Error? PageError = null,
    RunSchemaTestsResult? TestRunResult = null)
{
    public int TotalSchemas => Schemas.Count;

    public int TotalVersions
    {
        get { return Schemas.Sum(s => s.Versions.Count); }
    }

    public int ActiveVersions
    {
        get { return Schemas.Count(s => s.ActiveVersion.HasValue); }
    }

    public int ValidatedUnscheduledCount
    {
        get
        {
            return Schemas
                .SelectMany(s => s.Versions)
                .Count(v => string.Equals(v.State.Name, "Validated", StringComparison.OrdinalIgnoreCase));
        }
    }

    public int ErrorReportVersionCount
    {
        get
        {
            return Schemas
                .SelectMany(s => s.Versions)
                .Count(v => v.HasErrorReport && !string.Equals(v.State.Name, "Deleted", StringComparison.OrdinalIgnoreCase));
        }
    }

    public int InvalidVersionCount
    {
        get
        {
            return Schemas
                .SelectMany(s => s.Versions)
                .Count(v => string.Equals(v.State.Name, "Invalid", StringComparison.OrdinalIgnoreCase));
        }
    }

    public int UnvalidatedVersionCount
    {
        get
        {
            return Schemas
                .SelectMany(s => s.Versions)
                .Count(v => string.Equals(v.State.Name, "Unvalidated", StringComparison.OrdinalIgnoreCase));
        }
    }

    public NotificationBannerViewModel? SuccessBannerModel => NotificationBannerViewModel.Success(SuccessBanner);

    public NotificationBannerViewModel? ValidatedUnscheduledBannerModel =>
        ValidatedUnscheduledCount > 0
            ? NotificationBannerViewModel.Info(
                message: AdminUiMessages.UnscheduledVersionsBannerMessage(ValidatedUnscheduledCount),
                title: AdminUiMessages.UnscheduledVersionsBannerTitle,
                detail: AdminUiMessages.UnscheduledVersionsBannerDetail)
            : null;

    public NotificationBannerViewModel? ErrorReportAttentionBannerModel =>
        ErrorReportVersionCount > 0
            ? NotificationBannerViewModel.Info(
                message: AdminUiMessages.ErrorReportAttentionBannerMessage(ErrorReportVersionCount, InvalidVersionCount, UnvalidatedVersionCount),
                title: AdminUiMessages.ErrorReportAttentionBannerTitle,
                detail: AdminUiMessages.ErrorReportAttentionBannerDetail)
            : null;

    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(PageError);

    public bool HasTestRunResult => TestRunResult is not null;

    public DashboardTestRunSummaryViewModel? TestRunSummary
    {
        get { return _testRunSummary ??= DashboardTestRunSummaryViewModel.From(TestRunResult); }
    }

    private DashboardTestRunSummaryViewModel? _testRunSummary;

    public bool HasSchemas => Schemas.Count > 0;

    public bool NoSchemas => Schemas.Count == 0;

    public IReadOnlyList<SchemaSectionCardViewModel> SchemaCards
    {
        get { return _schemaCards ??= Schemas.Select(s => SchemaSectionCardViewModel.From(s)).ToList(); }
    }

    private IReadOnlyList<SchemaSectionCardViewModel>? _schemaCards;
}
