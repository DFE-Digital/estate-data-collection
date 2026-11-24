namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record GovUkTabItemViewModel(
    string Id,
    string Label,
    bool IsSelected,
    string Href,
    string ListItemCssClass,
    string PanelCssClass)
{
    public static GovUkTabItemViewModel Create(string id, string label, bool isSelected)
    {
        return new GovUkTabItemViewModel(
            Id: id,
            Label: label,
            IsSelected: isSelected,
            Href: $"#{id}",
            ListItemCssClass: isSelected
                ? "govuk-tabs__list-item govuk-tabs__list-item--selected"
                : "govuk-tabs__list-item",
            PanelCssClass: isSelected
                ? "govuk-tabs__panel"
                : "govuk-tabs__panel govuk-tabs__panel--hidden");
    }
}