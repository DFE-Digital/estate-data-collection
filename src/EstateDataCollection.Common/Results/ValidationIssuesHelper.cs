namespace EstateDataCollection.Common.Results;

public static class ValidationIssuesHelper
{
    public static IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>> EmptyIssues { get; } =
        new Dictionary<RuleSeverity, IReadOnlyList<ValidationError>>
        {
            [RuleSeverity.Critical] = [],
            [RuleSeverity.Error] = [],
            [RuleSeverity.Warning] = [],
            [RuleSeverity.Info] = []
        };

    public static IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>> GroupBySeverity(
        IEnumerable<ValidationError>? errors)
    {
        if (errors is null)
        {
            return EmptyIssues;
        }

        var critical = new List<ValidationError>();
        var error = new List<ValidationError>();
        var warning = new List<ValidationError>();
        var info = new List<ValidationError>();

        foreach (var item in errors)
        {
            if (item is null)
            {
                continue;
            }

            switch (item.Severity)
            {
                case RuleSeverity.Critical:
                    critical.Add(item);
                    break;
                case RuleSeverity.Warning:
                    warning.Add(item);
                    break;
                case RuleSeverity.Info:
                    info.Add(item);
                    break;
                default:
                    error.Add(item);
                    break;
            }
        }

        if (critical.Count == 0 && error.Count == 0 && warning.Count == 0 && info.Count == 0)
        {
            return EmptyIssues;
        }

        return new Dictionary<RuleSeverity, IReadOnlyList<ValidationError>>
        {
            [RuleSeverity.Critical] = critical,
            [RuleSeverity.Error] = error,
            [RuleSeverity.Warning] = warning,
            [RuleSeverity.Info] = info
        };
    }

    public static IReadOnlyList<ValidationError> GetBlockingIssues(
        IEnumerable<ValidationError>? errors,
        RuleSeverity failOnMinimumSeverity = RuleSeverity.Error)
    {
        if (errors is null)
        {
            return [];
        }

        return errors
            .Where(e => e is not null && e.Severity.IsAtOrAbove(failOnMinimumSeverity))
            .ToArray();
    }

    public static bool HasIssuesAtOrAbove(
        IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? issues,
        RuleSeverity threshold = RuleSeverity.Error)
    {
        if (issues is null || issues.Count == 0)
        {
            return false;
        }

        foreach (var (severity, list) in issues)
        {
            if (severity.IsAtOrAbove(threshold) && list is { Count: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasIssuesAtOrAbove(
        IEnumerable<ValidationError>? errors,
        RuleSeverity threshold = RuleSeverity.Error)
    {
        return errors is not null && errors.Any(e => e is not null && e.Severity.IsAtOrAbove(threshold));
    }
}
