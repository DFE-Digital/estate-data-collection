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
    public int TotalSchemas => Schemas.Count;

    public int TotalVersions => Schemas.Sum(s => s.Versions.Count);

    public int ActiveVersions => Schemas.Count(s => s.ActiveVersion.HasValue);

    public int ValidatedUnscheduledCount =>
        Schemas
            .SelectMany(s => s.Versions)
            .Count(v => string.Equals(v.State.Name, "Validated", StringComparison.OrdinalIgnoreCase));

    public NotificationBannerViewModel? SuccessBannerModel =>
        NotificationBannerViewModel.Success(SuccessBanner);

    public NotificationBannerViewModel? ValidatedUnscheduledBannerModel =>
        ValidatedUnscheduledCount > 0
            ? NotificationBannerViewModel.Info(
                message: $"{ValidatedUnscheduledCount} validated schema version(s) are currently unscheduled.",
                title: "New version(s) ready to schedule",
                detail: "Contracts promoted from draft-schemas or submitted via the workbench remain in Validated state until you configure an effective window or activate them.")
            : null;

    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(PageError);

    public bool HasTestRunResult => TestRunResult is not null;

    public DashboardTestRunSummaryViewModel? TestRunSummary =>
        _testRunSummary ??= DashboardTestRunSummaryViewModel.From(TestRunResult);
    private DashboardTestRunSummaryViewModel? _testRunSummary;

    public bool HasSchemas => Schemas.Count > 0;

    public bool NoSchemas => Schemas.Count == 0;

    public IReadOnlyList<SchemaSectionCardViewModel> SchemaCards =>
        _schemaCards ??= Schemas.Select(s => SchemaSectionCardViewModel.From(s)).ToList();
    private IReadOnlyList<SchemaSectionCardViewModel>? _schemaCards;
}
