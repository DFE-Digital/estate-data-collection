using EstateDataCollection.Common.Markers;
using System.ComponentModel.DataAnnotations;

namespace EstateDataCollection.AzureFunctions.Middleware;

public class RequestSizeLimitOptions : IOption<RequestSizeLimitOptions>
{
    public static string SectionName => "Security:RequestSizeLimit";

    [Range(1, 100)]
    public int MaxSizeMb { get; init; } = 15;

    public long MaxSizeBytes => (long)MaxSizeMb * 1024 * 1024;
}