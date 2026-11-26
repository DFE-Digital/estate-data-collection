namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record GovUkTabsViewModel(
    string ContainerId,
    IReadOnlyList<GovUkTabItemViewModel> Tabs,
    string? DefaultTabId = null,
    string Title = "Contents")
{
    public bool HasTabs => Tabs.Count > 0;

    public GovUkTabItemViewModel? Find(string id)
    {
        return Tabs.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
    }

    public static GovUkTabsViewModel Create(
        string containerId,
        IEnumerable<(string Id, string Label, bool Visible)> definitions,
        string? preferredSelectedId = null,
        string title = "Contents")
    {
        var visible = definitions.Where(d => d.Visible).ToList();
        if (visible.Count == 0)
        {
            return new GovUkTabsViewModel(containerId, [], null, title);
        }

        var selectedId = !string.IsNullOrWhiteSpace(preferredSelectedId) &&
                         visible.Any(v => string.Equals(v.Id, preferredSelectedId, StringComparison.Ordinal))
            ? preferredSelectedId
            : visible[0].Id;

        var tabs = visible
            .Select(v => GovUkTabItemViewModel.Create(
                v.Id,
                v.Label,
                string.Equals(v.Id, selectedId, StringComparison.Ordinal)))
            .ToList();

        var defaultTabId = !string.Equals(selectedId, visible[0].Id, StringComparison.Ordinal)
            ? selectedId
            : null;

        return new GovUkTabsViewModel(containerId, tabs, defaultTabId, title);
    }
}
