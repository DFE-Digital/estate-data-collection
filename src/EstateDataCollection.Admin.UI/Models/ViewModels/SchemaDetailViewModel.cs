using System.Globalization;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaDetailViewModel(
    AdminPageMeta Page,
    SchemaVersionDetail? Detail,
    string? SuccessBanner = null,
    Error? PageError = null)
{
    public NotificationBannerViewModel? SuccessBannerModel => NotificationBannerViewModel.Success(SuccessBanner);

    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(PageError);

    public bool HasDetail => Detail is not null;

    public string SchemaName => Detail?.SchemaName ?? string.Empty;

    public string VersionDisplay => Detail is null ? string.Empty : $"v{Detail.Version}";

    public string NextVersionDisplay => Detail is null ? string.Empty : $"v{Detail.Version + 1}";

    public string BackToSchemaUrl => Detail is null ? AppRoutes.AdminUi.Dashboard : AppRoutes.AdminUi.SchemaVersions(Detail.SchemaName);

    public bool IsRetired => Detail is not null && string.Equals(Detail.State.Name, "Retired", StringComparison.OrdinalIgnoreCase);

    public bool IsInvalid => Detail is not null && string.Equals(Detail.State.Name, "Invalid", StringComparison.OrdinalIgnoreCase);

    public bool IsUnvalidated => Detail is not null && string.Equals(Detail.State.Name, "Unvalidated", StringComparison.OrdinalIgnoreCase);

    public bool IsDeleted =>
        Detail is not null &&
        (string.Equals(Detail.State.Name, "Deleted", StringComparison.OrdinalIgnoreCase) ||
         Detail.DeletedAtUtc.HasValue);

    public bool CanModifyLifecycle => Detail is not null && !IsRetired && !IsDeleted && !IsInvalid && !IsUnvalidated;

    public bool CanRunTests => Detail is not null && !IsDeleted;

    public bool CanSignOff => Detail is not null && IsUnvalidated;

    public bool CanDelete => Detail is not null && !IsDeleted;

    public string ScheduleUrl =>
        Detail is null
            ? string.Empty
            : AppRoutes.AdminUi.SchemaSchedule(Detail.SchemaName, Detail.Version);

    public string TestsUrl =>
        Detail is null
            ? string.Empty
            : AppRoutes.AdminUi.SchemaTests(Detail.SchemaName, Detail.Version);

    public string RunTestsActionUrl =>
        Detail is null
            ? string.Empty
            : AppRoutes.AdminUi.SchemaTestsRun(Detail.SchemaName, Detail.Version);

    public string SignOffActionUrl =>
        Detail is null
            ? string.Empty
            : AppRoutes.AdminUi.SchemaSignOff(Detail.SchemaName, Detail.Version);

    public string DeleteActionUrl =>
        Detail is null
            ? string.Empty
            : AppRoutes.AdminUi.SchemaDelete(Detail.SchemaName, Detail.Version);

    public int ValidFixturesCount => Detail?.ValidFixtures?.Count ?? 0;

    public int InvalidFixturesCount => Detail?.InvalidFixtures?.Count ?? 0;

    public int StoredTestsCount => ValidFixturesCount + InvalidFixturesCount;

    public LifecycleTagViewModel? StateTag => Detail is null ? null : LifecycleTagViewModel.From(Detail.State.Name);

    public string AcceptsTrafficDisplay =>
        Detail?.CanAcceptTraffic == true
            ? "Yes (public submissions permitted)"
            : "No";

    public string EffectiveFromDisplay => Detail?.EffectiveFromUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Unscheduled";

    public string EffectiveToDisplay => Detail?.EffectiveToUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Open-ended";

    public bool HasIdentifierConfig => Detail?.IdentifierConfig is not null;

    public bool NoIdentifierConfig => !HasIdentifierConfig;

    public string IdentifierType => Detail?.IdentifierConfig?.Type ?? string.Empty;

    public string IdentifierPointerDisplay => Detail?.IdentifierConfig?.JsonPointer ?? "none";

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

    private SchemaValidationPolicy EffectivePolicy => Detail?.ValidationPolicy ?? SchemaValidationPolicy.Default;

    public string FailOnMinimumSeverityDisplay => EffectivePolicy.FailOnMinimumSeverity.ToString();

    public string WorkflowPipelineKeyDisplay => EffectivePolicy.WorkflowPipelineKey ?? "Default";

    public string CrossSubmissionDraftPolicyDisplay => EffectivePolicy.CrossSubmissionDraftPolicy.ToString();

    public string NumericToleranceOverrideDisplay => EffectivePolicy.NumericToleranceOverride?.ToString(CultureInfo.InvariantCulture) ?? "None";

    public bool HasSignedOff => !string.IsNullOrWhiteSpace(Detail?.SignedOffBy);

    public string SignedOffDisplay
    {
        get
        {
            if (!HasSignedOff)
            {
                return string.Empty;
            }

            return Detail?.SignedOffAtUtc is { } atUtc
                ? $"{Detail.SignedOffBy} ({atUtc:yyyy-MM-dd HH:mm:ss 'UTC'})"
                : Detail!.SignedOffBy!;
        }
    }

    public string DeletedAtDisplay => Detail?.DeletedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Deleted";

    public bool HasErrorReport => Detail?.HasErrorReport == true || Detail?.ErrorReport is not null;

    public bool HasErrorReportErrors
    {
        get
        {
            if (IsInvalid || HasErrorReportSyntaxErrors || HasErrorReportFixtureErrors)
            {
                return true;
            }

            var issues = Detail?.ErrorReport?.Issues;
            return issues is not null &&
                   ((issues.TryGetValue(RuleSeverity.Critical, out var critical) && critical.Count > 0) ||
                    (issues.TryGetValue(RuleSeverity.Error, out var errors) && errors.Count > 0));
        }
    }

    public string ErrorReportHeading =>
        HasErrorReportErrors
            ? "Schema Error Report (.error.json) — Validation Failed"
            : "Schema Diagnostic Report (.error.json) — Warnings Recorded";

    public string ErrorReportTagText
    {
        get
        {
            if (IsInvalid)
            {
                return "Invalid (.error.json)";
            }

            return HasErrorReportErrors ? "Errors (.error.json)" : "Warnings (.error.json)";
        }
    }

    public string ErrorReportTagCssClass => HasErrorReportErrors ? "govuk-tag govuk-tag--red" : "govuk-tag govuk-tag--yellow";

    public string ErrorReportSummaryText => Detail?.ErrorReport?.Summary ?? Detail?.ErrorSummary ?? string.Empty;

    public bool HasErrorReportSummary => !string.IsNullOrWhiteSpace(ErrorReportSummaryText);

    public bool HasErrorReportRecordedAt => Detail?.ErrorReport is not null;

    public string ErrorReportRecordedAtDisplay => Detail?.ErrorReport?.RecordedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? string.Empty;

    public IReadOnlyList<string> ErrorReportSyntaxErrors => Detail?.ErrorReport?.SyntaxErrors ?? [];

    public bool HasErrorReportSyntaxErrors => ErrorReportSyntaxErrors.Count > 0;

    public IReadOnlyList<string> ErrorReportFixtureErrors => Detail?.ErrorReport?.FixtureErrors ?? [];

    public bool HasErrorReportFixtureErrors => ErrorReportFixtureErrors.Count > 0;

    public IReadOnlyList<string> ErrorReportWarnings => Detail?.ErrorReport?.Warnings ?? [];

    public bool HasErrorReportWarnings => ErrorReportWarnings.Count > 0;

    public IReadOnlyList<SeverityIssueGroupViewModel> ErrorReportIssueGroups
    {
        get { return _errorReportIssueGroups ??= SeverityIssueGroupViewModel.FromDictionary(Detail?.ErrorReport?.Issues); }
    }

    private IReadOnlyList<SeverityIssueGroupViewModel>? _errorReportIssueGroups;

    public bool HasErrorReportIssues => ErrorReportIssueGroups.Count > 0;

    public bool HasLastTestRun => Detail?.LastTestRunAtUtc.HasValue == true;

    public string LastTestRunTagText => Detail?.LastTestRunPassed == true ? "Passed" : "Failed";

    public string LastTestRunTagCssClass =>
        Detail?.LastTestRunPassed == true
            ? "govuk-tag govuk-tag--green"
            : "govuk-tag govuk-tag--red";

    public string LastTestRunTimestampDisplay =>
        Detail?.LastTestRunAtUtc is { } atUtc
            ? $"({atUtc:yyyy-MM-dd HH:mm} UTC)"
            : string.Empty;

    public string Sha256Hash => Detail?.Sha256Hash ?? string.Empty;

    public string CreatedAtDisplay => Detail?.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? string.Empty;

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

    public bool HasRawJsonSchema => !string.IsNullOrWhiteSpace(Detail?.RawJsonSchema);

    public string RawJsonSchema => Detail?.RawJsonSchema ?? string.Empty;

    public GovUkTabsViewModel Tabs
    {
        get
        {
            return _tabs ??= GovUkTabsViewModel.Create(
                "schema-detail-tabs",
                [
                    ("schema-fields", $"Fields ({FieldsViewer.TopLevelFieldCount})", true),
                    ("schema-quality-rules", $"Quality rules ({QualityRulesViewer.RuleCount})", true),
                    ("schema-validation-engines", "Validation engines", EngineDiagnostics.HasContent),
                    ("schema-raw-json", "Raw JSON Schema", HasRawJsonSchema)
                ]);
        }
    }

    private GovUkTabsViewModel? _tabs;

    public GovUkTabItemViewModel? FieldsTab => Tabs.Find("schema-fields");

    public GovUkTabItemViewModel? QualityRulesTab => Tabs.Find("schema-quality-rules");

    public GovUkTabItemViewModel? ValidationEnginesTab => Tabs.Find("schema-validation-engines");

    public GovUkTabItemViewModel? RawJsonSchemaTab => Tabs.Find("schema-raw-json");
}
