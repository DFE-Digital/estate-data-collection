using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Infrastructure.Registry;

public sealed partial class BlobSchemaRegistryInstrumentation(
    ILogger<BlobSchemaRegistry> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Memory cache hit for schema {SchemaName} v{Version}"
    )]
    public partial void SchemaCacheHit(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Memory cache miss for schema {SchemaName} v{Version}, loading from Blob"
    )]
    public partial void SchemaCacheMiss(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Schema blob not found for {SchemaName} v{Version}"
    )]
    public partial void SchemaBlobNotFound(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Schema {SchemaName} v{Version} loaded from Blob container and cached"
    )]
    public partial void SchemaLoadedFromBlob(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Uploaded schema and metadata blobs for {SchemaName} v{Version}"
    )]
    public partial void SchemaSavedToBlob(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to upload schema blobs for {SchemaName} v{Version}: {Error}"
    )]
    public partial void SchemaSaveToBlobFailed(string schemaName, int version, string error);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Candidate schema verification completed. Valid: {IsValid}, SyntaxErrors: {SyntaxErrorCount}, FixtureErrors: {FixtureErrorCount}"
    )]
    public partial void CandidateVerificationCompleted(bool isValid, int syntaxErrorCount, int fixtureErrorCount);
}
