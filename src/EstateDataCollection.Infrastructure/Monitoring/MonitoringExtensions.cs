using System.Reflection;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using EstateDataCollection.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EstateDataCollection.Infrastructure.Monitoring;

public static class MonitoringExtensions
{
    private const string LocalIdentifier = $"{nameof(EstateDataCollection)}.*";
    private const string WorkerIdentifier = "Microsoft.Azure.Functions.Worker";
    
    /// <summary>
    /// Configures OpenTelemetry distributed tracing, metrics, and structured logging.
    /// Supports Azure Application Insights (via Azure Monitor exporter and Entra ID) and OTLP collectors.
    /// Also configures console logging for local runs and container streaming logs.
    /// </summary>
    public static IServiceCollection AddMonitoring(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggingBuilder logging,
        MonitoringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        options ??= new();

        logging.AddConsole();

        var appInsightsConnectionString = GetConfigurationItem(configuration,options.AppInsightsEnvironmentVariable);
        var otlpEndpoint = GetConfigurationItem(configuration, options.OtlpEndpointEnvironmentVariable);
        var hasAppInsights = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        var hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        if (!hasAppInsights && !hasOtlp)
        {
            return services;
        }

        var serviceName = options.ServiceName;
        var info = GetVersionInfo(configuration, options.ServiceDescriberEnvironmentVariable);
        
        if (configuration.GetValue<bool>(options.AppInsightsVersionedEnvironmentVariable))
        {
            serviceName += ":" + info.Version;
        }

        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: info.InformativeVersion)
            .AddAttributes([
                new KeyValuePair<string, object>("service.environment", info.Environment),
                new KeyValuePair<string, object>(AppVersionProvider.Keys.Version, info.Version),
                new KeyValuePair<string, object>(AppVersionProvider.Keys.InformativeVersion, info.InformativeVersion),
                new KeyValuePair<string, object>(AppVersionProvider.Keys.Sha, info.CommitSha),
            ]);

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource(serviceName)
                    .AddSource(LocalIdentifier)
                    .AddSource(WorkerIdentifier)
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation(opt => opt.RecordException = true);

                if (hasAppInsights)
                {
                    tracing.AddAzureMonitorTraceExporter(o =>
                    {
                        o.ConnectionString = appInsightsConnectionString;
                        o.Credential = new DefaultAzureCredential();
                    });
                }

                if (hasOtlp)
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder)
                    .AddMeter(serviceName)
                    .AddMeter(LocalIdentifier)
                    .AddMeter(WorkerIdentifier)
                    .AddHttpClientInstrumentation();

                if (hasAppInsights)
                {
                    metrics.AddAzureMonitorMetricExporter(o =>
                    {
                        o.ConnectionString = appInsightsConnectionString;
                        o.Credential = new DefaultAzureCredential();
                    });
                }

                if (hasOtlp)
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!));
                }
            });

        logging.AddOpenTelemetry(telemetry =>
        {
            telemetry.SetResourceBuilder(resourceBuilder);
            telemetry.IncludeFormattedMessage = true;
            telemetry.IncludeScopes = true;

            if (hasAppInsights)
            {
                telemetry.AddAzureMonitorLogExporter(o =>
                {
                    o.ConnectionString = appInsightsConnectionString;
                    o.Credential = new DefaultAzureCredential();
                });
            }

            if (hasOtlp)
            {
                telemetry.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!));
            }
        });

        return services;
    }

    private static string? GetConfigurationItem(IConfiguration configuration, string key)
    {
        return configuration[key]
               ?? configuration[$"Values:{key}"]
               ?? Environment.GetEnvironmentVariable(key);
    }
    
    private sealed record EnvironmentInfo(string Version, string InformativeVersion, string CommitSha, string Environment);

    private static EnvironmentInfo GetVersionInfo(IConfiguration configuration, string environmentKey)
    {
        var env = GetConfigurationItem(configuration, environmentKey) ?? "UNDECLARED";
        return new EnvironmentInfo(AppVersionProvider.InformativeVersion, AppVersionProvider.InformativeVersion, AppVersionProvider.Sha, env);
    }
}
