using System.ComponentModel.DataAnnotations;
using EstateDataCollection.Common.Markers;

namespace EstateDataCollection.AzureFunctions.AdminUi.Options;

public sealed class AdminUiOptions : IOption<AdminUiOptions>
{
    public static string SectionName
    {
        get { return "AdminUi"; }
    }

    public const string DirectMode = "Direct";
    public const string HttpProxyMode = "HttpProxy";

    [Required]
    public string ExecutionMode { get; set; } = DirectMode;

    public string AdminApiBaseUrl { get; set; } = "http://localhost:7072/api";

    public string? AdminApiFunctionKey { get; set; }

    public bool IsHttpProxyMode
    {
        get { return string.Equals(ExecutionMode?.Trim(), HttpProxyMode, StringComparison.OrdinalIgnoreCase); }
    }
}
