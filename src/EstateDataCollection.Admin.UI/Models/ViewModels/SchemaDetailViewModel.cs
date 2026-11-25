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

    public string NextVersionDisplay
    {
        get { return Detail is null ? string.Empty : $"v{Detail.Version + 1}"; }
    }

    public string BackToSchemaUrl
    {
        get
        {
            return Detail is null ? AppRoutes.AdminUi.Dashboard : AppRoutes.AdminUi.SchemaVersions(Detail.SchemaName);
        }
    }

    public bool IsRetired
    {
        get { return Detail is not null && string.Equals(Detail.State.Name, "Retired", StringComparison.OrdinalIgnoreCase); }
    }

    public bool IsInvalid
    {
        get { return Detail is not null && string.Equals(Detail.State.Name, "Invalid", StringComparison.OrdinalIgnoreCase); }
    }

    public bool IsUnvalidated
    {
        get { return Detail is not null && string.Equals(Detail.State.Name, "Unvalidated", StringComparison.OrdinalIgnoreCase); }
    }

    public bool IsDeleted
    {
        get
        {
            return Detail is not null &&
                   (string.Equals(Detail.State.Name, "Deleted", StringComparison.OrdinalIgnoreCase) ||
                    Detail.DeletedAtUtc.HasValue);
        }
    }

    public bool CanModifyLifecycle
    {
        get
        {
            return Detail is not null && !IsRetired && !IsDeleted && !IsInvalid && !IsUnvalidated;
        }
    }

    public bool CanRunTests
    {
        get { return Detail is not null && !IsDeleted; }
    }

    public bool CanSignOff
    {
        get { return Detail is not null && IsUnvalidated; }
    }

    public bool CanDelete
    {
        get { return Detail is not null && !IsDeleted; }
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

    public string SignOffActionUrl
    {
        get
        {
            return Detail is null
                ? string.Empty
                : AppRoutes.AdminUi.SchemaSignOff(Detail.SchemaName, Detail.Version);
        }
    }

    public string DeleteActionUrl
    {
        get
        {
            return Detail is null
                ? string.Empty
                : AppRoutes.AdminUi.SchemaDelete(Detail.SchemaName, Detail.Version);
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

    private SchemaValidationPolicy EffectivePolicy => Detail?.ValidationPolicy ?? SchemaValidationPolicy.Default;

    public string FailOnMinimumSeverityDisplay
    {
        get { return EffectivePolicy.FailOnMinimumSeverity.ToString(); }
    }

    public string WorkflowPipelineKeyDisplay
    {
        get { return EffectivePolicy.WorkflowPipelineKey ?? "Default"; }
    }

    public string CrossSubmissionDraftPolicyDisplay
    {
        get { return EffectivePolicy.CrossSubmissionDraftPolicy.ToString(); }
    }

    public string NumericToleranceOverrideDisplay
    {
        get
        {
            return EffectivePolicy.NumericToleranceOverride?.ToString(CultureInfo.InvariantCulture) ?? "None";
        }
    }

    public bool HasSignedOff
    {
        get { return !string.IsNullOrWhiteSpace(Detail?.SignedOffBy); }
    }

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

    public string DeletedAtDisplay
    {
        get { return Detail?.DeletedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Deleted"; }
    }

    public bool HasErrorReport
    {
        get { return Detail?.HasErrorReport == true || Detail?.ErrorReport is not null; }
    }

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

    public string ErrorReportHeading
    {
        get
        {
            return HasErrorReportErrors
                ? "Schema Error Report (.error.json) — Validation Failed"
                : "Schema Diagnostic Report (.error.json) — Warnings Recorded";
        }
    }

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

    public string ErrorReportTagCssClass
    {
        get { return HasErrorReportErrors ? "govuk-tag govuk-tag--red" : "govuk-tag govuk-tag--yellow"; }
    }

    public string ErrorReportSummaryText
    {
        get { return Detail?.ErrorReport?.Summary ?? Detail?.ErrorSummary ?? string.Empty; }
    }

    public bool HasErrorReportSummary
    {
        get { return !string.IsNullOrWhiteSpace(ErrorReportSummaryText); }
    }

    public bool HasErrorReportRecordedAt
    {
        get { return Detail?.ErrorReport is not null; }
    }

    public string ErrorReportRecordedAtDisplay
    {
        get { return Detail?.ErrorReport?.RecordedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? string.Empty; }
    }

    public IReadOnlyList<string> ErrorReportSyntaxErrors
    {
        get { return Detail?.ErrorReport?.SyntaxErrors ?? []; }
    }

    public bool HasErrorReportSyntaxErrors
    {
        get { return ErrorReportSyntaxErrors.Count > 0; }
    }

    public IReadOnlyList<string> ErrorReportFixtureErrors
    {
        get { return Detail?.ErrorReport?.FixtureErrors ?? []; }
    }

    public bool HasErrorReportFixtureErrors
    {
        get { return ErrorReportFixtureErrors.Count > 0; }
    }

    public IReadOnlyList<string> ErrorReportWarnings
    {
        get { return Detail?.ErrorReport?.Warnings ?? []; }
    }

    public bool HasErrorReportWarnings
    {
        get { return ErrorReportWarnings.Count > 0; }
    }

    public IReadOnlyList<SeverityIssueGroupViewModel> ErrorReportIssueGroups
    {
        get { return _errorReportIssueGroups ??= SeverityIssueGroupViewModel.FromDictionary(Detail?.ErrorReport?.Issues); }
    }

    private IReadOnlyList<SeverityIssueGroupViewModel>? _errorReportIssueGroups;

    public bool HasErrorReportIssues
    {
        get { return ErrorReportIssueGroups.Count > 0; }
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

    public GovUkTabItemViewModel? FieldsTab
    {
        get { return Tabs.Find("schema-fields"); }
    }

    public GovUkTabItemViewModel? QualityRulesTab
    {
        get { return Tabs.Find("schema-quality-rules"); }
    }

    public GovUkTabItemViewModel? ValidationEnginesTab
    {
        get { return Tabs.Find("schema-validation-engines"); }
    }

    public GovUkTabItemViewModel? RawJsonSchemaTab
    {
        get { return Tabs.Find("schema-raw-json"); }
    }
}
