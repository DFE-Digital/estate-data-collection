using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record WorkbenchVerifyViewModel(
    bool HasRun,
    VerifySchemaResult? Result = null,
    Error? Error = null)
{
    public bool ShowPanel => HasRun;

    public bool HasError => HasRun && Error is not null;

    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(Error);

    public bool HasResult => HasRun && Error is null && Result is not null;

    public bool HasReport => HasRun && Result is not null;

    public NotificationBannerViewModel? SuccessBanner =>
        Result is null
            ? null
            : NotificationBannerViewModel.Success(
                message: Result.Message,
                title: "Verification passed",
                detail:
                "Contract syntax, identifier alignment, quality rules, and any provided sample fixtures passed dry-run verification.");

    public bool HasWarnings => Result?.Warnings is { Count: > 0 };

    public int WarningCount => Result?.Warnings?.Count ?? 0;

    public IReadOnlyList<string> Warnings => Result?.Warnings ?? [];

    public bool AllWarningsAreUnknownInvalidFixtures
    {
        get
        {
            if (!HasWarnings || Result?.InvalidFixtureOutcomes is not { Count: > 0 } outcomes)
            {
                return false;
            }

            var unknownCount = outcomes.Count(o => o.ExpectedErrors is not { Count: > 0 });
            return unknownCount > 0 && unknownCount == WarningCount;
        }
    }

    public bool ShowWarningBullets => HasWarnings && !AllWarningsAreUnknownInvalidFixtures;

    public IReadOnlyList<SeverityIssueGroupViewModel> IssueGroups
    {
        get { return _issueGroups ??= SeverityIssueGroupViewModel.FromDictionary(Result?.Issues); }
    }

    private IReadOnlyList<SeverityIssueGroupViewModel>? _issueGroups;

    public bool HasIssueGroups => IssueGroups.Count > 0;

    public IReadOnlyList<ValidationErrorItemViewModel> AnalyzerFindings
    {
        get
        {
            return _analyzerFindings ??= (Result?.AnalyzerFindings ?? [])
                .Select(SeverityIssueGroupViewModel.ToItem)
                .ToList();
        }
    }

    private IReadOnlyList<ValidationErrorItemViewModel>? _analyzerFindings;

    public bool HasAnalyzerFindings => AnalyzerFindings.Count > 0;

    public IReadOnlyList<string> SyntaxErrors => Result?.SyntaxErrors ?? [];

    public bool HasSyntaxErrors => SyntaxErrors.Count > 0;

    public IReadOnlyList<string> FixtureErrors => Result?.FixtureErrors ?? [];

    public bool HasFixtureErrors => FixtureErrors.Count > 0;

    public bool HasInvalidFixtureOutcomes => Result?.InvalidFixtureOutcomes is { Count: > 0 };

    public IReadOnlyList<VerifyInvalidOutcomeViewModel> InvalidFixtureOutcomes
    {
        get { return _invalidFixtureOutcomes ??= BuildInvalidFixtureOutcomes(); }
    }

    private IReadOnlyList<VerifyInvalidOutcomeViewModel>? _invalidFixtureOutcomes;

    public ValidationEngineDiagnosticsViewModel EngineDiagnostics
    {
        get
        {
            return _engineDiagnostics ??= ValidationEngineDiagnosticsViewModel.FromTraces(
                Result?.EngineTraces,
                heading: "Validation Engine Diagnostics",
                subtitle: "Per-engine execution outcomes across schema syntax, quality rules, identifier extraction, and golden fixtures.");
        }
    }

    private ValidationEngineDiagnosticsViewModel? _engineDiagnostics;

    public bool HasResolvedIdentifierConfig => Result?.ResolvedIdentifierConfig is not null;

    public bool IsIdentifierDisabled => Result?.ResolvedIdentifierConfig?.IsDisabled == true;

    public bool IsIdentifierEnabled => Result?.ResolvedIdentifierConfig is { IsDisabled: false };

    public string IdentifierType => Result?.ResolvedIdentifierConfig?.Type ?? string.Empty;

    public string IdentifierPointerDisplay => Result?.ResolvedIdentifierConfig?.JsonPointer ?? "default";

    public string IdentifierRequiredDisplay => Result?.ResolvedIdentifierConfig?.Required == true ? "Yes" : "No";

    public GovUkTabsViewModel Tabs
    {
        get
        {
            return _tabs ??= GovUkTabsViewModel.Create(
                "workbench-verify-tabs",
                [
                    ("verify-summary", "Summary & fixtures", true),
                    ("verify-engine-diagnostics", "Engine diagnostics", EngineDiagnostics.HasContent)
                ]);
        }
    }

    private GovUkTabsViewModel? _tabs;

    public bool ShowTabs => Tabs.Tabs.Count > 1;

    public bool HasSummaryDetails =>
        HasIssueGroups ||
        HasAnalyzerFindings ||
        HasSyntaxErrors ||
        HasFixtureErrors ||
        HasInvalidFixtureOutcomes ||
        HasResolvedIdentifierConfig;

    public bool NoSummaryDetails => !HasSummaryDetails;

    public GovUkTabItemViewModel? SummaryTab => Tabs.Find("verify-summary");

    public GovUkTabItemViewModel? EngineDiagnosticsTab => Tabs.Find("verify-engine-diagnostics");

    private IReadOnlyList<VerifyInvalidOutcomeViewModel> BuildInvalidFixtureOutcomes()
    {
        var outcomes = Result?.InvalidFixtureOutcomes;
        if (outcomes is null || outcomes.Count == 0)
        {
            return [];
        }

        return outcomes
            .Select(o =>
            {
                var hasExpected = o.ExpectedErrors is { Count: > 0 };
                return new VerifyInvalidOutcomeViewModel(
                    Title: $"Invalid Fixture #{o.Index + 1}",
                    HasDescription: !string.IsNullOrWhiteSpace(o.Description),
                    Description: o.Description,
                    StatusTagText: hasExpected
                        ? "Matched expected error(s)"
                        : "Failed \u2014 unknown if expected",
                    StatusTagCssClass: hasExpected
                        ? "govuk-tag govuk-tag--green"
                        : "govuk-tag govuk-tag--yellow",
                    TriggeredErrors: o.TriggeredErrors);
            })
            .ToList();
    }
}
