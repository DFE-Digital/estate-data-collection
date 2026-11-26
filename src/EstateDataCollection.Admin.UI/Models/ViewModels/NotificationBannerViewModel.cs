using EstateDataCollection.Common.Messages;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record NotificationBannerViewModel(
    string CssClass,
    string Role,
    string HeadingText,
    string Message,
    string? Detail = null)
{
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public static NotificationBannerViewModel? From(
        string variant,
        string? message,
        string? title = null,
        string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var isSuccess = string.Equals(variant, "success", StringComparison.OrdinalIgnoreCase);
        var cssClass = isSuccess
            ? "govuk-notification-banner govuk-notification-banner--success"
            : "govuk-notification-banner";
        var role = isSuccess ? "alert" : "region";
        var headingText = !string.IsNullOrWhiteSpace(title)
            ? title
            : (isSuccess ? AdminUiMessages.BannerHeadingSuccess : AdminUiMessages.BannerHeadingImportant);

        return new NotificationBannerViewModel(
            CssClass: cssClass,
            Role: role,
            HeadingText: headingText,
            Message: message,
            Detail: detail);
    }

    public static NotificationBannerViewModel? Success(
        string? message,
        string? title = null,
        string? detail = null)
    {
        return From("success", message, title, detail);
    }

    public static NotificationBannerViewModel? Info(
        string? message,
        string? title = null,
        string? detail = null)
    {
        return From("info", message, title, detail);
    }
}
