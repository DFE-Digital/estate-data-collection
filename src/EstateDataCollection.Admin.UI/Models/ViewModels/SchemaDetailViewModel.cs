using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaDetailViewModel(
    AdminPageMeta Page,
    SchemaVersionDetail? Detail,
    string? SuccessBanner = null,
    Error? PageError = null)
{
    public NotificationBannerViewModel? SuccessBannerModel =>
        NotificationBannerViewModel.Success(SuccessBanner);

    public ErrorSummaryViewModel? ErrorSummary =>
        ErrorSummaryViewModel.From(PageError);

    public bool HasDetail => Detail is not null;

    public string SchemaName => Detail?.SchemaName ?? string.Empty;

    public string VersionDisplay => Detail is null ? string.Empty : $"v{Detail.Version}";

    public string BackToSchemaUrl =>
        Detail is null ? "/manage" : $"/manage/schemas/{Uri.EscapeDataString(Detail.SchemaName)}";

    public bool CanModifyLifecycle =>
        Detail is not null &&
        !string.Equals(Detail.State.Name, "Retired", StringComparison.OrdinalIgnoreCase);

    public string ScheduleUrl =>
        Detail is null
            ? string.Empty
            : $"/manage/schemas/{Uri.EscapeDataString(Detail.SchemaName)}/{Detail.Version}/schedule";

    public string TestsUrl =>
        Detail is null
            ? string.Empty
            : $"/manage/schemas/{Uri.EscapeDataString(Detail.SchemaName)}/{Detail.Version}/tests";

    public string RunTestsActionUrl =>
        Detail is null
            ? string.Empty
            : $"/manage/schemas/{Uri.EscapeDataString(Detail.SchemaName)}/{Detail.Version}/tests/run";

    public int ValidFixturesCount => Detail?.ValidFixtures?.Count ?? 0;

    public int InvalidFixturesCount => Detail?.InvalidFixtures?.Count ?? 0;

    public int StoredTestsCount => ValidFixturesCount + InvalidFixturesCount;

    public LifecycleTagViewModel? StateTag =>
        Detail is null ? null : LifecycleTagViewModel.From(Detail.State.Name);

    public string AcceptsTrafficDisplay =>
        Detail?.CanAcceptTraffic == true
            ? "Yes (public submissions permitted)"
            : "No";

    public string EffectiveFromDisplay =>
        Detail?.EffectiveFromUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Unscheduled";

    public string EffectiveToDisplay =>
        Detail?.EffectiveToUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Open-ended";

    public bool HasIdentifierConfig => Detail?.IdentifierConfig is not null;

    public bool NoIdentifierConfig => !HasIdentifierConfig;

    public string IdentifierType => Detail?.IdentifierConfig?.Type ?? string.Empty;

    public string IdentifierPointerDisplay => Detail?.IdentifierConfig?.JsonPointer ?? "none";

    public string IdentifierRequiredDisplay =>
        Detail?.IdentifierConfig?.Required switch
        {
            true => "Yes",
            false => "No",
            null => "Default"
        };

    public bool HasLastTestRun => Detail?.LastTestRunAtUtc.HasValue == true;

    public string LastTestRunTagText =>
        Detail?.LastTestRunPassed == true ? "Passed" : "Failed";

    public string LastTestRunTagCssClass =>
        Detail?.LastTestRunPassed == true
            ? "govuk-tag govuk-tag--green"
            : "govuk-tag govuk-tag--red";

    public string LastTestRunTimestampDisplay =>
        Detail?.LastTestRunAtUtc is { } atUtc
            ? $"({atUtc:yyyy-MM-dd HH:mm} UTC)"
            : string.Empty;

    public string Sha256Hash => Detail?.Sha256Hash ?? string.Empty;

    public string CreatedAtDisplay =>
        Detail?.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? string.Empty;

    public FieldsViewerViewModel FieldsViewer =>
        _fieldsViewer ??= FieldsViewerViewModel.From(Detail?.Fields);
    private FieldsViewerViewModel? _fieldsViewer;

    public QualityRulesViewerViewModel QualityRulesViewer =>
        _qualityRulesViewer ??= QualityRulesViewerViewModel.From(Detail?.QualityRules);
    private QualityRulesViewerViewModel? _qualityRulesViewer;

    public bool HasRawJsonSchema => !string.IsNullOrWhiteSpace(Detail?.RawJsonSchema);

    public string RawJsonSchema => Detail?.RawJsonSchema ?? string.Empty;
}
