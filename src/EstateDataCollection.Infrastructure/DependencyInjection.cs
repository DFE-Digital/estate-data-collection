using Azure.Identity;
using Azure.Storage.Blobs;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static System.StringComparison;

namespace EstateDataCollection.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var localAssembly = typeof(DependencyInjection).Assembly;

        services.AddMemoryCache();
        services.AddAzureBlobStorage(configuration);
        
        services
            .AddConfigOptions(localAssembly)
            .Scan(scan => scan
                .FromAssemblies(localAssembly)
                .AddClasses(classes => classes.AssignableTo<IInfrastructure>())
                .AsImplementedInterfaces()
                .WithScopedLifetime()
                .AddClasses(classes => classes.AssignableTo<IInstrumentation>())
                .AsSelf()
                .WithTransientLifetime());

        return services;
    }

    public static void AddAzureBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolve user-assigned managed identity client ID if configured
        var clientId = configuration["AZURE_CLIENT_ID"];

        var credential = !string.IsNullOrWhiteSpace(clientId)
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId })
            : new DefaultAzureCredential();

        // Check for Entra ID / Managed Identity application blob storage endpoint
        var blobEndpoint = configuration["EDC_STORAGE_BLOB_ENDPOINT"];

        var accountName = configuration["EDC_STORAGE_ACCOUNT_NAME"];

        // Check for explicit connection string (not AzureWebJobsStorage)
        var blobConnectionString = configuration.GetConnectionString("BlobStorage");

        if (!string.IsNullOrWhiteSpace(blobEndpoint))
        {
            // Entra ID with User-Assigned Managed Identity via Endpoint URI
            services.AddSingleton(new BlobServiceClient(new Uri(blobEndpoint), credential));
        }
        else if (!string.IsNullOrWhiteSpace(accountName))
        {
            // Entra ID with User-Assigned Managed Identity via Account Name
            services.AddSingleton(new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"),
                credential));
        }
        else if (!string.IsNullOrWhiteSpace(blobConnectionString))
        {
            // Explicit connection string
            services.AddSingleton(new BlobServiceClient(blobConnectionString));
        }
        else
        {
            // Local development fallback (Azurite)
            var azuriteStorage = configuration["AzureWebJobsStorage"]
                                 ?? configuration["Values:AzureWebJobsStorage"]
                                 ?? "UseDevelopmentStorage=true";

            if (azuriteStorage.StartsWith("DefaultEndpointsProtocol=", OrdinalIgnoreCase) ||
                azuriteStorage.StartsWith("UseDevelopmentStorage=", OrdinalIgnoreCase))
            {
                services.AddSingleton(new BlobServiceClient(azuriteStorage));
            }
            else
            {
                services.AddSingleton(new BlobServiceClient("UseDevelopmentStorage=true"));
            }
        }
    }
}
