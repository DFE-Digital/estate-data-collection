using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record DashboardViewModel(
    AdminPageMeta Page,
    IReadOnlyList<SchemaCatalogueEntry> Schemas,
    string? SuccessBanner = null,
    Error? PageError = null,
    RunSchemaTestsResult? TestRunResult = null)
{
    public int TotalSchemas
    {
        get { return Schemas.Count; }
    }

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

    public NotificationBannerViewModel? SuccessBannerModel
    {
        get { return NotificationBannerViewModel.Success(SuccessBanner); }
    }

    public NotificationBannerViewModel? ValidatedUnscheduledBannerModel
    {
        get
        {
            return ValidatedUnscheduledCount > 0
                ? NotificationBannerViewModel.Info(
                    message: $"{ValidatedUnscheduledCount} validated schema version(s) are currently unscheduled.",
                    title: "New version(s) ready to schedule",
                    detail:
                    "Contracts promoted from draft-schemas or submitted via the workbench remain in Validated state until you configure an effective window or activate them.")
                : null;
        }
    }

    public NotificationBannerViewModel? ErrorReportAttentionBannerModel
    {
        get
        {
            return ErrorReportVersionCount > 0
                ? NotificationBannerViewModel.Info(
                    message: $"{ErrorReportVersionCount} schema version(s) have a persisted .error.json diagnostic report ({InvalidVersionCount} Invalid, {UnvalidatedVersionCount} awaiting sign-off).",
                    title: "Schema diagnostics require review",
                    detail:
                    "Inspect highlighted versions to review .error.json findings, sign off Unvalidated contracts with warnings, or delete broken versions so a fix can be published to version + 1.")
                : null;
        }
    }

    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(PageError); }
    }

    public bool HasTestRunResult
    {
        get { return TestRunResult is not null; }
    }

    public DashboardTestRunSummaryViewModel? TestRunSummary
    {
        get { return _testRunSummary ??= DashboardTestRunSummaryViewModel.From(TestRunResult); }
    }

    private DashboardTestRunSummaryViewModel? _testRunSummary;

    public bool HasSchemas
    {
        get { return Schemas.Count > 0; }
    }

    public bool NoSchemas
    {
        get { return Schemas.Count == 0; }
    }

    public IReadOnlyList<SchemaSectionCardViewModel> SchemaCards
    {
        get { return _schemaCards ??= Schemas.Select(s => SchemaSectionCardViewModel.From(s)).ToList(); }
    }

    private IReadOnlyList<SchemaSectionCardViewModel>? _schemaCards;
}
