using Azure.Identity;
using Azure.Storage.Blobs;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static System.StringComparison;

namespace EstateDataCollection.Infrastructure;

public static class DependencyInjection
{
    private const BlobClientOptions.ServiceVersion AzuriteMaxSupportedServiceVersion =
        BlobClientOptions.ServiceVersion.V2025_11_05;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var localAssembly = typeof(DependencyInjection).Assembly;
        var appAssembly = typeof(Application.DependencyInjection).Assembly;

        services.AddMemoryCache();
        services.AddAzureBlobStorage(configuration);

        services
            .AddConfigOptions(localAssembly, appAssembly)
            .Scan(scan => scan
                .FromAssemblies(localAssembly)
                .AddClasses(classes => classes.AssignableToAny(
                    typeof(ISchemaValidator),
                    typeof(IQualityRuleEngine),
                    typeof(IRuleContextEnricher),
                    typeof(IOdcsContractParser),
                    typeof(IBusinessLogicValidator))
                    .Where(t => t != typeof(Validation.JsonLogicValidator)))
                .AsImplementedInterfaces()
                .WithSingletonLifetime()
                .AddClasses(classes => classes
                    .AssignableTo<IInfrastructure>()
                    .Where(t =>
                        !typeof(ISchemaValidator).IsAssignableFrom(t) &&
                        !typeof(IQualityRuleEngine).IsAssignableFrom(t) &&
                        !typeof(IRuleContextEnricher).IsAssignableFrom(t) &&
                        !typeof(IOdcsContractParser).IsAssignableFrom(t) &&
                        !typeof(IBusinessLogicValidator).IsAssignableFrom(t)))
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
            services.AddSingleton(CreateBlobServiceClient(blobConnectionString));
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
                services.AddSingleton(CreateBlobServiceClient(azuriteStorage));
            }
            else
            {
                services.AddSingleton(CreateBlobServiceClient("UseDevelopmentStorage=true"));
            }
        }
    }

    private static BlobServiceClient CreateBlobServiceClient(string connectionString)
    {
        if (IsAzuriteConnectionString(connectionString))
        {
            return new BlobServiceClient(connectionString, new BlobClientOptions(AzuriteMaxSupportedServiceVersion));
        }

        return new BlobServiceClient(connectionString);
    }

    private static bool IsAzuriteConnectionString(string connectionString)
    {
        return connectionString.Contains("UseDevelopmentStorage=true", OrdinalIgnoreCase) ||
               connectionString.Contains("AccountName=devstoreaccount1", OrdinalIgnoreCase) ||
               connectionString.Contains("127.0.0.1:10000", OrdinalIgnoreCase) ||
               connectionString.Contains("localhost:10000", OrdinalIgnoreCase);
    }
}
