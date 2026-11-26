using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Common.Routing;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record DashboardTestRunSummaryViewModel(
    string AppVersion,
    string SummaryTagText,
    string SummaryTagCssClass,
    IReadOnlyList<DashboardTestRunItemViewModel> Results)
{
    public bool HasResults => Results.Count > 0;
    public bool NoResults => Results.Count == 0;
    
    public static DashboardTestRunSummaryViewModel? From(RunSchemaTestsResult? run)
    {
        if (run is null)
        {
            return null;
        }

        var allPassed = run.FailedCount == 0;
        var warningSchemaCount = run.Results.Count(r => r.Passed && r.Warnings is { Count: > 0 });

        string summaryTagText;
        string summaryTagCssClass;
        if (allPassed && warningSchemaCount == 0)
        {
            summaryTagText = $"{run.PassedCount} / {run.TotalSchemasChecked} Passed";
            summaryTagCssClass = "govuk-tag govuk-tag--green";
        }
        else if (allPassed)
        {
            summaryTagText = $"{run.PassedCount} / {run.TotalSchemasChecked} Passed (warnings)";
            summaryTagCssClass = "govuk-tag govuk-tag--yellow";
        }
        else
        {
            summaryTagText = $"{run.FailedCount} Failed ({run.PassedCount} Passed)";
            summaryTagCssClass = "govuk-tag govuk-tag--red";
        }

        var items = run.Results
            .Select(item =>
            {
                var hasWarnings = item.Warnings is { Count: > 0 };
                string resultTagText;
                string resultTagCssClass;
                if (item.Passed && !hasWarnings)
                {
                    resultTagText = "Passed";
                    resultTagCssClass = "govuk-tag govuk-tag--green";
                }
                else if (item.Passed)
                {
                    resultTagText = "Passed (warnings)";
                    resultTagCssClass = "govuk-tag govuk-tag--yellow";
                }
                else
                {
                    resultTagText = "Failed";
                    resultTagCssClass = "govuk-tag govuk-tag--red";
                }

                string? detailsText = null;
                if (item.Errors.Count > 0)
                {
                    detailsText = string.Join("; ", item.Errors);
                }
                else if (hasWarnings)
                {
                    detailsText = string.Join("; ", item.Warnings!);
                }

                var engineSummary = ValidationEngineDiagnosticsViewModel.FormatCompactSummary(item.EngineTraces);

                return new DashboardTestRunItemViewModel(
                    SchemaDisplayName: $"{item.SchemaName} v{item.Version}",
                    TestsUrl: AppRoutes.AdminUi.SchemaTests(item.SchemaName, item.Version),
                    StateTag: LifecycleTagViewModel.From(item.State),
                    CountsSummary: $"{item.ValidFixtureCount} valid, {item.InvalidFixtureCount} invalid, {item.BusinessRuleCount} rule(s)",
                    ResultTagText: resultTagText,
                    ResultTagCssClass: resultTagCssClass,
                    HasDetailsText: detailsText is not null,
                    DetailsText: detailsText,
                    EngineTracesSummary: engineSummary);
            })
            .ToList();

        return new DashboardTestRunSummaryViewModel(
            AppVersion: run.AppVersion,
            SummaryTagText: summaryTagText,
            SummaryTagCssClass: summaryTagCssClass,
            Results: items);
    }
}
