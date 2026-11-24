using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;

public sealed partial class ScheduleSchemaUseCaseInstrumentation(
    ILogger<ScheduleSchemaUseCase> logger) : IInstrumentation
{
    [LoggerMessage(
        EventId = 1700,
        Level = LogLevel.Information,
        Message = "Updating schedule/lifecycle for schema {SchemaName} v{Version}")]
    public partial void UpdatingSchemaSchedule(string schemaName, int version);

    [LoggerMessage(
        EventId = 1701,
        Level = LogLevel.Information,
        Message = "Schema {SchemaName} v{Version} transitioned to {State} (EffectiveFromUtc={EffectiveFromUtc}, EffectiveToUtc={EffectiveToUtc})")]
    public partial void SchemaScheduleUpdated(
        string schemaName,
        int version,
        string state,
        DateTimeOffset? effectiveFromUtc,
        DateTimeOffset? effectiveToUtc);

    [LoggerMessage(
        EventId = 1702,
        Level = LogLevel.Warning,
        Message = "Invalid schedule/lifecycle transition for schema {SchemaName} v{Version}: {Reason}")]
    public partial void SchemaScheduleFailed(string schemaName, int version, string reason);
}
