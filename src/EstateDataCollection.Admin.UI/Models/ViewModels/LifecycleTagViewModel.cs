using EstateDataCollection.Common.Messages;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record LifecycleTagViewModel(
    string Text,
    string CssClass)
{
    public static LifecycleTagViewModel From(string? stateName)
    {
        var normalized = (stateName ?? AdminUiMessages.LifecycleStateUndetermined).Trim();
        var modifier = normalized.ToLowerInvariant() switch
        {
            "active" => "govuk-tag--green",
            "scheduled" => "govuk-tag--blue",
            "validated" => "govuk-tag--purple",
            "unvalidated" or "deprecated" => "govuk-tag--yellow",
            "invalid" or "retired" => "govuk-tag--red",
            "deleted" or "draft" => "govuk-tag--grey",
            _ => "govuk-tag--grey"
        };

        return new LifecycleTagViewModel(normalized, $"govuk-tag {modifier}");
    }
}
