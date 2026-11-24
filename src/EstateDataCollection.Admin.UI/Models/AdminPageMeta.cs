namespace EstateDataCollection.Admin.UI.Models;

public sealed record AdminPageMeta(
    string Title,
    string ActiveNav,
    string? SignedInUser = null,
    string AppVersion = "")
{
    public string DocumentTitle
    {
        get { return $"{Title} - Estate Data Collection - Department for Education"; }
    }

    public bool HasSignedInUser
    {
        get { return !string.IsNullOrWhiteSpace(SignedInUser); }
    }

    public bool HasAppVersion
    {
        get { return !string.IsNullOrWhiteSpace(AppVersion); }
    }

    public string SchemasNavLinkCssClass
    {
        get
        {
            return string.Equals(ActiveNav, "schemas", StringComparison.OrdinalIgnoreCase)
                ? "dfe-service-nav__link dfe-service-nav__link--active"
                : "dfe-service-nav__link";
        }
    }

    public string WorkbenchNavLinkCssClass
    {
        get
        {
            return string.Equals(ActiveNav, "workbench", StringComparison.OrdinalIgnoreCase)
                ? "dfe-service-nav__link dfe-service-nav__link--active"
                : "dfe-service-nav__link";
        }
    }

    public string StatisticsNavLinkCssClass
    {
        get
        {
            return string.Equals(ActiveNav, "statistics", StringComparison.OrdinalIgnoreCase)
                ? "dfe-service-nav__link dfe-service-nav__link--active"
                : "dfe-service-nav__link";
        }
    }

    public string SubmissionsNavLinkCssClass
    {
        get
        {
            return string.Equals(ActiveNav, "submissions", StringComparison.OrdinalIgnoreCase)
                ? "dfe-service-nav__link dfe-service-nav__link--active"
                : "dfe-service-nav__link";
        }
    }
}
