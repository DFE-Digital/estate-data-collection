using Azure.Core;
using static Microsoft.Extensions.Configuration.AzureAppConfiguration.KeyFilter;
using static Microsoft.Extensions.Configuration.AzureAppConfiguration.LabelFilter;

namespace EstateDataCollection.Infrastructure.Configuration;

public class AzureAppConfigurationOptions
{
    public string EndpointEnvironmentVariable { get; init; } = "AZURE_APPCONFIGURATION_ENDPOINT";
    public string RefreshKey { get; init; } = "Edc:Sentinel";
    
    public TokenCredential? LocalCredential { get; init; }
    
    public string KeyFilter { get; init; } = Any;
    public string LabelFilter { get; init; } = Null;
}