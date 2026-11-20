namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record DashboardTestRunItemViewModel(
    string SchemaDisplayName,
    string TestsUrl,
    LifecycleTagViewModel StateTag,
    string CountsSummary,
    string ResultTagText,
    string ResultTagCssClass,
    bool HasDetailsText,
    string? DetailsText);