namespace EstateDataCollection.Admin.UI.Models;

public sealed record AdminPageMeta(
    string Title,
    string ActiveNav,
    string? SignedInUser = null,
    string AppVersion = "")
{
    public string DocumentTitle => $"{Title} - Estate Data Collection - Department for Education";

    public bool HasSignedInUser => !string.IsNullOrWhiteSpace(SignedInUser);

    public bool HasAppVersion => !string.IsNullOrWhiteSpace(AppVersion);

    public string SchemasNavLinkCssClass =>
        string.Equals(ActiveNav, "schemas", StringComparison.OrdinalIgnoreCase)
            ? "dfe-service-nav__link dfe-service-nav__link--active"
            : "dfe-service-nav__link";

    public string WorkbenchNavLinkCssClass =>
        string.Equals(ActiveNav, "workbench", StringComparison.OrdinalIgnoreCase)
            ? "dfe-service-nav__link dfe-service-nav__link--active"
            : "dfe-service-nav__link";

    public string StatisticsNavLinkCssClass =>
        string.Equals(ActiveNav, "statistics", StringComparison.OrdinalIgnoreCase)
            ? "dfe-service-nav__link dfe-service-nav__link--active"
            : "dfe-service-nav__link";

    public string SubmissionsNavLinkCssClass =>
        string.Equals(ActiveNav, "submissions", StringComparison.OrdinalIgnoreCase)
            ? "dfe-service-nav__link dfe-service-nav__link--active"
            : "dfe-service-nav__link";
}
