using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EstateDataCollection.Infrastructure.Configuration;

public static class AzureAppConfigurationExtensions
{
    /// <summary>
    /// Connects to Azure App Configuration if an endpoint is configured in environment variables or configuration.
    /// Uses Managed Identity (TokenCredential) for zero-trust authentication.
    /// </summary>
    public static IConfigurationBuilder AddAppConfiguration(
        this IConfigurationBuilder builder,
        IServiceCollection services,
        AzureAppConfigurationOptions? options = null)
    {
        options ??= new();
        
        var endpoint = Environment.GetEnvironmentVariable(options.EndpointEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable("AZURE_APP_CONFIGURATION_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("EDC_APPCONFIG_ENDPOINT");
        
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return builder;
        }
        
        services.AddAzureAppConfiguration();

        var cred = options.LocalCredential ?? new DefaultAzureCredential();

        builder.AddAzureAppConfiguration(config =>
        {
            config.Connect(new Uri(endpoint), cred)
                   .Select(options.KeyFilter, options.LabelFilter)
                   .ConfigureKeyVault(kv => kv.SetCredential(cred))
                   .ConfigureRefresh(refresh =>
                   {
                       refresh.Register(options.RefreshKey, refreshAll: true)
                              .SetRefreshInterval(TimeSpan.FromMinutes(5));
                   });
        }, optional: true);

        return builder;
    }
}
