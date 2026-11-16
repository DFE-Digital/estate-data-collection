using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;

public sealed partial class IngestDraftSchemaUseCaseInstrumentation(
    ILogger<IngestDraftSchemaUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Ingesting draft schema blob {BlobPath}"
    )]
    public partial void IngestingDraftBlob(string blobPath);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Skipping companion or diagnostic blob {BlobPath} in draft container"
    )]
    public partial void SkippingCompanionBlob(string blobPath);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Draft rules blob {BlobPath} for {SchemaName} v{Version} validated and staged awaiting companion .schema.json"
    )]
    public partial void DraftRulesStagedAwaitingSchema(string blobPath, string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Draft schema {BlobPath} ({SchemaName} v{Version}) failed verification with {ErrorCount} errors: {Errors}"
    )]
    public partial void DraftVerificationFailed(string blobPath, string schemaName, int version, int errorCount, string errors);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Draft schema {BlobPath} promoted to active schemas container as {SchemaName} v{Version} (SHA-256: {Sha256})"
    )]
    public partial void DraftSchemaPromoted(string blobPath, string schemaName, int version, string sha256);
}
