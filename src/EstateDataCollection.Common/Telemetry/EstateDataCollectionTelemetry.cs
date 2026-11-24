using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EstateDataCollection.Common.Telemetry;

/// <summary>
/// Central OpenTelemetry <see cref="ActivitySource"/> and <see cref="Meter"/> definitions
/// matching the <c>EstateDataCollection.*</c> wildcard registered in monitoring setup.
/// </summary>
public static class EstateDataCollectionTelemetry
{
    public const string ApplicationSourceName = "EstateDataCollection.Application";
    public const string ApplicationMeterName = "EstateDataCollection.Application";

    public static readonly ActivitySource ApplicationActivitySource = new(ApplicationSourceName);
    public static readonly Meter ApplicationMeter = new(ApplicationMeterName);

    public static readonly Histogram<double> ProcessDurationMs =
        ApplicationMeter.CreateHistogram<double>(
            "edc.submission.process.duration",
            unit: "ms",
            description: "Duration of individual submission pipeline process steps in milliseconds.");

    public static readonly Counter<long> ProcessExecutions =
        ApplicationMeter.CreateCounter<long>(
            "edc.submission.process.executions",
            unit: "{execution}",
            description: "Count of submission pipeline process step executions by action, process, engine, and outcome.");

    public static readonly Counter<long> SubmissionAuditEvents =
        ApplicationMeter.CreateCounter<long>(
            "edc.submission.audit.events",
            unit: "{event}",
            description: "Count of submission audit events recorded by action, outcome status, and HTTP status.");
}
