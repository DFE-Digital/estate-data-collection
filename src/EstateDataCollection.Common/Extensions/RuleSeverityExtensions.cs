using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Common.Extensions;

public static class RuleSeverityExtensions
{
    public static string ToKeyString(this RuleSeverity severity)
    {
        return severity switch
        {
            RuleSeverity.Critical => "critical",
            RuleSeverity.Error => "error",
            RuleSeverity.Warning => "warning",
            RuleSeverity.Info => "info",
            _ => "error"
        };
    }

    public static bool IsAtOrAbove(this RuleSeverity severity, RuleSeverity threshold)
    {
        return (int)severity <= (int)threshold;
    }

    public static bool TryParse(string? raw, out RuleSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            severity = RuleSeverity.Error;
            return false;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "critical":
            case "crit":
            case "blocker":
            case "fatal":
                severity = RuleSeverity.Critical;
                return true;

            case "error":
            case "err":
                severity = RuleSeverity.Error;
                return true;

            case "warning":
            case "warn":
                severity = RuleSeverity.Warning;
                return true;

            case "info":
            case "information":
            case "advisory":
                severity = RuleSeverity.Info;
                return true;

            default:
                if (int.TryParse(raw.Trim(), out var numeric) && Enum.IsDefined(typeof(RuleSeverity), numeric))
                {
                    severity = (RuleSeverity)numeric;
                    return true;
                }

                severity = RuleSeverity.Error;
                return false;
        }
    }

    public static RuleSeverity ParseOrDefault(string? raw, RuleSeverity fallback = RuleSeverity.Error)
    {
        return TryParse(raw, out var parsed) ? parsed : fallback;
    }
}