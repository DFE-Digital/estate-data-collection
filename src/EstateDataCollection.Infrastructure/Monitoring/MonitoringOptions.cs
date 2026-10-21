namespace EstateDataCollection.Infrastructure.Monitoring;

public class MonitoringOptions
{
    public string ServiceName { get; init; } = "estate-data-collection-service";
    public string AppInsightsEnvironmentVariable { get; init; } = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public string AppInsightsVersionedEnvironmentVariable { get; init; } = "APPLICATIONINSIGHTS_INCLUDE_VERSION";
    public string OtlpEndpointEnvironmentVariable { get; init; } = "OTEL_EXPORTER_OTLP_ENDPOINT";
}