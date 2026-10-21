using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Schema.SubmitSchema;

public sealed partial class SubmitSchemaUseCaseInstrumentation(
    ILogger<SubmitSchemaUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Submitting schema contract {SchemaName} v{Version}"
    )]
    public partial void SubmittingSchema(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Schema {SchemaName} v{Version} already exists in registry"
    )]
    public partial void SchemaAlreadyExists(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Schema {SchemaName} v{Version} candidate verification failed with {ErrorCount} errors: {Errors}"
    )]
    public partial void SchemaCandidateVerificationFailed(string schemaName, int version, int errorCount, string errors);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Invalid activation schedule for schema {SchemaName} v{Version}: {Error}"
    )]
    public partial void SchemaInvalidSchedule(string schemaName, int version, string error);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Schema {SchemaName} v{Version} successfully committed to registry with SHA-256 {Sha256}"
    )]
    public partial void SchemaSaved(string schemaName, int version, string sha256);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to save schema {SchemaName} v{Version} to registry: {Error}"
    )]
    public partial void SchemaSaveFailed(string schemaName, int version, string error);
}
