using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Messages;
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
                title: AdminUiMessages.VerificationPassedTitle,
                detail: AdminUiMessages.VerificationPassedDetail);

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
        get { return field ??= SeverityIssueGroupViewModel.FromDictionary(Result?.Issues); }
    }

    public bool HasIssueGroups => IssueGroups.Count > 0;

    public IReadOnlyList<ValidationErrorItemViewModel> AnalyserFindings
    {
        get
        {
            return field ??= (Result?.AnalyserFindings ?? [])
                .Select(SeverityIssueGroupViewModel.ToItem)
                .ToList();
        }
    }

    public bool HasAnalyserFindings => AnalyserFindings.Count > 0;

    public IReadOnlyList<string> SyntaxErrors => Result?.SyntaxErrors ?? [];

    public bool HasSyntaxErrors => SyntaxErrors.Count > 0;

    public IReadOnlyList<string> FixtureErrors => Result?.FixtureErrors ?? [];

    public bool HasFixtureErrors => FixtureErrors.Count > 0;

    public bool HasInvalidFixtureOutcomes => Result?.InvalidFixtureOutcomes is { Count: > 0 };

    public IReadOnlyList<VerifyInvalidOutcomeViewModel> InvalidFixtureOutcomes
    {
        get { return field ??= BuildInvalidFixtureOutcomes(); }
    }

    public ValidationEngineDiagnosticsViewModel EngineDiagnostics
    {
        get
        {
            return field ??= ValidationEngineDiagnosticsViewModel.FromTraces(
                Result?.EngineTraces,
                heading: AdminUiMessages.EngineDiagnosticsDefaultHeading,
                subtitle: AdminUiMessages.VerifyEngineDiagnosticsSubtitle);
        }
    }

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
            return field ??= GovUkTabsViewModel.Create(
                "workbench-verify-tabs",
                [
                    ("verify-summary", AdminUiMessages.VerifySummaryTabLabel, true),
                    ("verify-engine-diagnostics", AdminUiMessages.VerifyEngineDiagnosticsTabLabel, EngineDiagnostics.HasContent)
                ]);
        }
    }

    public bool ShowTabs => Tabs.Tabs.Count > 1;

    public bool HasSummaryDetails =>
        HasIssueGroups ||
        HasAnalyserFindings ||
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
                    Title: AdminUiMessages.InvalidFixtureTitle(o.Index + 1),
                    HasDescription: !string.IsNullOrWhiteSpace(o.Description),
                    Description: o.Description,
                    StatusTagText: hasExpected
                        ? AdminUiMessages.FixtureStatusMatchedExpectedErrors
                        : AdminUiMessages.FixtureStatusFailedUnknownIfExpected,
                    StatusTagCssClass: hasExpected
                        ? "govuk-tag govuk-tag--green"
                        : "govuk-tag govuk-tag--yellow",
                    TriggeredErrors: o.TriggeredErrors);
            })
            .ToList();
    }
}
