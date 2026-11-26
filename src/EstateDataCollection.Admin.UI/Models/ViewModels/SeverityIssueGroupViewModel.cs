using EstateDataCollection.Common.Helpers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SeverityIssueGroupViewModel(
    RuleSeverity Severity,
    string SeverityLabel,
    string TagCssClass,
    int Count,
    IReadOnlyList<ValidationErrorItemViewModel> Items)
{
    private static readonly RuleSeverity[] OrderedSeverities =
    [
        RuleSeverity.Critical,
        RuleSeverity.Error,
        RuleSeverity.Warning,
        RuleSeverity.Info
    ];

    public static string GetSeverityTagCssClass(RuleSeverity severity)
    {
        return severity switch
        {
            RuleSeverity.Critical => "govuk-tag govuk-tag--red",
            RuleSeverity.Error => "govuk-tag govuk-tag--orange",
            RuleSeverity.Warning => "govuk-tag govuk-tag--yellow",
            RuleSeverity.Info => "govuk-tag govuk-tag--blue",
            _ => "govuk-tag govuk-tag--grey"
        };
    }

    public static ValidationErrorItemViewModel ToItem(ValidationError error)
    {
        var msg = !string.IsNullOrWhiteSpace(error.RuleId)
            ? $"[{error.RuleId}] {error.Message}"
            : error.Message;

        return new ValidationErrorItemViewModel(
            HasPropertyPath: !string.IsNullOrWhiteSpace(error.PropertyPath),
            PropertyPath: error.PropertyPath,
            Message: msg,
            SeverityName: error.Severity.ToString(),
            SeverityTagCssClass: GetSeverityTagCssClass(error.Severity),
            EngineId: error.EngineId);
    }

    public static ValidationErrorItemViewModel ToItem(SubmissionAuditValidationIssue issue)
    {
        var msg = !string.IsNullOrWhiteSpace(issue.RuleId)
            ? $"[{issue.RuleId}] {issue.Message}"
            : issue.Message;

        return new ValidationErrorItemViewModel(
            HasPropertyPath: !string.IsNullOrWhiteSpace(issue.PropertyPath),
            PropertyPath: issue.PropertyPath,
            Message: msg,
            SeverityName: issue.Severity.ToString(),
            SeverityTagCssClass: GetSeverityTagCssClass(issue.Severity));
    }

    public static IReadOnlyList<SeverityIssueGroupViewModel> FromDictionary(
        IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? issues)
    {
        if (issues is null || issues.Count == 0)
        {
            return [];
        }

        var groups = new List<SeverityIssueGroupViewModel>();
        foreach (var severity in OrderedSeverities)
        {
            if (!issues.TryGetValue(severity, out var list) || list is not { Count: > 0 })
            {
                continue;
            }

            var items = list.Select(ToItem).ToList();
            groups.Add(new SeverityIssueGroupViewModel(
                Severity: severity,
                SeverityLabel: severity.ToString(),
                TagCssClass: GetSeverityTagCssClass(severity),
                Count: items.Count,
                Items: items));
        }

        return groups;
    }

    public static IReadOnlyList<SeverityIssueGroupViewModel> FromErrors(IEnumerable<ValidationError>? errors)
    {
        if (errors is null)
        {
            return [];
        }

        return FromDictionary(ValidationIssuesHelper.GroupBySeverity(errors));
    }

    public static IReadOnlyList<SeverityIssueGroupViewModel> FromAuditIssues(
        IEnumerable<SubmissionAuditValidationIssue>? issues)
    {
        if (issues is null)
        {
            return [];
        }

        var mapped = issues.Select(i => new ValidationError(
            i.PropertyPath,
            i.Message,
            i.RuleId,
            i.Severity));

        return FromErrors(mapped);
    }
}
