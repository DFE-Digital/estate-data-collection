using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;

public sealed partial class RunSchemaTestsUseCaseInstrumentation(
    ILogger<RunSchemaTestsUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Running stored regression tests. SchemaFilter: {SchemaName}, VersionFilter: {Version}, IncludeRetired: {IncludeRetired}"
    )]
    public partial void RunningSchemaTests(string schemaName, int? version, bool includeRetired);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Schema regression tests completed on app version {AppVersion}: {TotalChecked} checked, {PassedCount} passed, {FailedCount} failed"
    )]
    public partial void SchemaTestsCompleted(string appVersion, int totalChecked, int passedCount, int failedCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Schema '{SchemaName}' v{Version} failed regression tests with {ErrorCount} error(s): {Errors}"
    )]
    public partial void SchemaTestFailed(string schemaName, int version, int errorCount, string errors);
}
