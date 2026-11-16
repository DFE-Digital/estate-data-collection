using System.ComponentModel.DataAnnotations;
using EstateDataCollection.Common.Markers;

namespace EstateDataCollection.Common.Web.Models.Options;

public class RequestSizeLimitOptions : IOption<RequestSizeLimitOptions>
{
    public static string SectionName => "Security:RequestSizeLimit";

    [Range(1, 100)]
    public int MaxSizeMb { get; init; } = 15;

    public long MaxSizeBytes => (long)MaxSizeMb * 1024 * 1024;
}
