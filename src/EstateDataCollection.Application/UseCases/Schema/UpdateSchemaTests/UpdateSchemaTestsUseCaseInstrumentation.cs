using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;

public sealed partial class UpdateSchemaTestsUseCaseInstrumentation(
    ILogger<UpdateSchemaTestsUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Updating stored test fixtures for {SchemaName} v{Version} ({ValidCount} valid, {InvalidCount} invalid)"
    )]
    public partial void UpdatingSchemaFixtures(string schemaName, int version, int validCount, int invalidCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Updated test fixtures for {SchemaName} v{Version} failed verification with {ErrorCount} error(s): {Errors}"
    )]
    public partial void SchemaFixtureUpdateVerificationFailed(string schemaName, int version, int errorCount, string errors);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Updated and verified stored test fixtures for {SchemaName} v{Version}: {ValidCount} valid, {InvalidCount} invalid"
    )]
    public partial void SchemaFixturesUpdated(string schemaName, int version, int validCount, int invalidCount);
}
