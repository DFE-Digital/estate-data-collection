using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaDetailViewModel(
    AdminPageMeta Page,
    SchemaVersionDetail? Detail,
    string? SuccessBanner = null,
    Error? PageError = null)
{
    public NotificationBannerViewModel? SuccessBannerModel
    {
        get { return NotificationBannerViewModel.Success(SuccessBanner); }
    }

    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(PageError); }
    }

    public bool HasDetail
    {
        get { return Detail is not null; }
    }

    public string SchemaName
    {
        get { return Detail?.SchemaName ?? string.Empty; }
    }

    public string VersionDisplay
    {
        get { return Detail is null ? string.Empty : $"v{Detail.Version}"; }
    }

    public string BackToSchemaUrl
    {
        get
        {
            return Detail is null ? AppRoutes.AdminUi.Dashboard : AppRoutes.AdminUi.SchemaVersions(Detail.SchemaName);
        }
    }

    public bool CanModifyLifecycle
    {
        get
        {
            return Detail is not null &&
                   !string.Equals(Detail.State.Name, "Retired", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string ScheduleUrl
    {
        get
        {
            return Detail is null
                ? string.Empty
                : AppRoutes.AdminUi.SchemaSchedule(Detail.SchemaName, Detail.Version);
        }
    }

    public string TestsUrl
    {
        get
        {
            return Detail is null
                ? string.Empty
                : AppRoutes.AdminUi.SchemaTests(Detail.SchemaName, Detail.Version);
        }
    }

    public string RunTestsActionUrl
    {
        get
        {
            return Detail is null
                ? string.Empty
                : AppRoutes.AdminUi.SchemaTestsRun(Detail.SchemaName, Detail.Version);
        }
    }

    public int ValidFixturesCount
    {
        get { return Detail?.ValidFixtures?.Count ?? 0; }
    }

    public int InvalidFixturesCount
    {
        get { return Detail?.InvalidFixtures?.Count ?? 0; }
    }

    public int StoredTestsCount
    {
        get { return ValidFixturesCount + InvalidFixturesCount; }
    }

    public LifecycleTagViewModel? StateTag
    {
        get { return Detail is null ? null : LifecycleTagViewModel.From(Detail.State.Name); }
    }

    public string AcceptsTrafficDisplay
    {
        get
        {
            return Detail?.CanAcceptTraffic == true
                ? "Yes (public submissions permitted)"
                : "No";
        }
    }

    public string EffectiveFromDisplay
    {
        get { return Detail?.EffectiveFromUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Unscheduled"; }
    }

    public string EffectiveToDisplay
    {
        get { return Detail?.EffectiveToUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Open-ended"; }
    }

    public bool HasIdentifierConfig
    {
        get { return Detail?.IdentifierConfig is not null; }
    }

    public bool NoIdentifierConfig
    {
        get { return !HasIdentifierConfig; }
    }

    public string IdentifierType
    {
        get { return Detail?.IdentifierConfig?.Type ?? string.Empty; }
    }

    public string IdentifierPointerDisplay
    {
        get { return Detail?.IdentifierConfig?.JsonPointer ?? "none"; }
    }

    public string IdentifierRequiredDisplay
    {
        get
        {
            return Detail?.IdentifierConfig?.Required switch
            {
                true => "Yes",
                false => "No",
                null => "Default"
            };
        }
    }

    public bool HasLastTestRun
    {
        get { return Detail?.LastTestRunAtUtc.HasValue == true; }
    }

    public string LastTestRunTagText
    {
        get { return Detail?.LastTestRunPassed == true ? "Passed" : "Failed"; }
    }

    public string LastTestRunTagCssClass
    {
        get
        {
            return Detail?.LastTestRunPassed == true
                ? "govuk-tag govuk-tag--green"
                : "govuk-tag govuk-tag--red";
        }
    }

    public string LastTestRunTimestampDisplay
    {
        get
        {
            return Detail?.LastTestRunAtUtc is { } atUtc
                ? $"({atUtc:yyyy-MM-dd HH:mm} UTC)"
                : string.Empty;
        }
    }

    public string Sha256Hash
    {
        get { return Detail?.Sha256Hash ?? string.Empty; }
    }

    public string CreatedAtDisplay
    {
        get { return Detail?.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? string.Empty; }
    }

    public FieldsViewerViewModel FieldsViewer
    {
        get { return _fieldsViewer ??= FieldsViewerViewModel.From(Detail?.Fields); }
    }

    private FieldsViewerViewModel? _fieldsViewer;

    public QualityRulesViewerViewModel QualityRulesViewer
    {
        get { return _qualityRulesViewer ??= QualityRulesViewerViewModel.From(Detail?.QualityRules); }
    }

    private QualityRulesViewerViewModel? _qualityRulesViewer;

    public ValidationEngineDiagnosticsViewModel EngineDiagnostics
    {
        get
        {
            return _engineDiagnostics ??= ValidationEngineDiagnosticsViewModel.FromDetail(
                Detail?.LastTestRunEngineTraces,
                Detail?.RegisteredEngines,
                Detail?.QualityRules,
                Detail?.IdentifierConfig);
        }
    }

    private ValidationEngineDiagnosticsViewModel? _engineDiagnostics;

    public bool HasRawJsonSchema
    {
        get { return !string.IsNullOrWhiteSpace(Detail?.RawJsonSchema); }
    }

    public string RawJsonSchema
    {
        get { return Detail?.RawJsonSchema ?? string.Empty; }
    }
}
