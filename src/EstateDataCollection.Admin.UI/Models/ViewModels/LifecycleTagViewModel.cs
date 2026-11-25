namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record LifecycleTagViewModel(
    string Text,
    string CssClass)
{
    public static LifecycleTagViewModel From(string? stateName)
    {
        var normalized = (stateName ?? "Unknown").Trim();
        var modifier = normalized.ToLowerInvariant() switch
        {
            "active" => "govuk-tag--green",
            "scheduled" => "govuk-tag--blue",
            "validated" => "govuk-tag--purple",
            "unvalidated" => "govuk-tag--yellow",
            "deprecated" => "govuk-tag--yellow",
            "invalid" => "govuk-tag--red",
            "retired" => "govuk-tag--red",
            "deleted" => "govuk-tag--grey",
            "draft" => "govuk-tag--grey",
            _ => "govuk-tag--grey"
        };

        return new LifecycleTagViewModel(normalized, $"govuk-tag {modifier}");
    }
}
