using EstateDataCollection.Common.Markers;
using EstateDataCollection.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.AzureFunctions;

public static class DependencyInjection
{
    public static IServiceCollection AddFunctions(this IServiceCollection services)
    {
        var localAssembly = typeof(DependencyInjection).Assembly;
        
        services
            .AddConfigOptions(localAssembly)
            .Scan(scan => scan
                .FromAssemblies(localAssembly)
                .AddClasses(classes => classes.AssignableTo<IInstrumentation>())
                .AsSelf()
                .WithTransientLifetime());

        return services;
    }
    
    public static IServiceCollection AddLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggingBuilder logging)
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);

        services.Configure<LoggerFilterOptions>(options =>
        {
            var defaultRule = options.Rules.FirstOrDefault(rule =>
                rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null)
            {
                options.Rules.Remove(defaultRule);
            }
        });

        if (configuration is IConfigurationBuilder configBuilder)
        {
            configBuilder.AddAppConfiguration(services);
        }

        var appInsightsConnectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
                                          ?? configuration["Values:APPLICATIONINSIGHTS_CONNECTION_STRING"]
                                          ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

        if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            services.AddApplicationInsightsTelemetryWorkerService(options =>
            {
                options.ConnectionString = appInsightsConnectionString;
            });
            services.ConfigureFunctionsApplicationInsights();
        }
        
        return services;
    }
}
