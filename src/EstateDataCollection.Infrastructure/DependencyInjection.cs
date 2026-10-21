using Azure.Identity;
using Azure.Storage.Blobs;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EstateDataCollection.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var localAssembly = typeof(DependencyInjection).Assembly;
        
        services.AddMemoryCache();
        
        var blobConnectionString = configuration.GetConnectionString("BlobStorage") 
            ?? configuration["AzureWebJobsStorage"]
            ?? configuration["Values:AzureWebJobsStorage"]
            ?? configuration["DEPLOYMENT_STORAGE_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? Environment.GetEnvironmentVariable("DEPLOYMENT_STORAGE_CONNECTION_STRING");

        var blobEndpoint = configuration["EDC_STORAGE_BLOB_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("EDC_STORAGE_BLOB_ENDPOINT");

        var accountName = configuration["AzureWebJobsStorage__accountName"]
            ?? configuration["EDC_STORAGE_ACCOUNT_NAME"]
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName")
            ?? Environment.GetEnvironmentVariable("EDC_STORAGE_ACCOUNT_NAME");

        if (!string.IsNullOrWhiteSpace(blobConnectionString))
        {
            services.AddSingleton(new BlobServiceClient(blobConnectionString));
        }
        else if (!string.IsNullOrWhiteSpace(blobEndpoint))
        {
            services.AddSingleton(new BlobServiceClient(new Uri(blobEndpoint), new DefaultAzureCredential()));
        }
        else if (!string.IsNullOrWhiteSpace(accountName))
        {
            services.AddSingleton(new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"), new DefaultAzureCredential()));
        }
        else
        {
            services.AddSingleton(new BlobServiceClient("UseDevelopmentStorage=true"));
        }
        
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
}
