using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Infrastructure.Validation;

public sealed partial class JsonSchemaNetValidatorInstrumentation(
    ILogger<JsonSchemaNetValidator> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Starting JSON schema contract validation against Draft 2020-12"
    )]
    public partial void ValidationStarted();

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "JSON schema contract validation passed successfully"
    )]
    public partial void ValidationPassed();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "JSON schema contract validation failed with {ErrorCount} errors"
    )]
    public partial void ValidationFailed(int errorCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "JSON schema contract validation encountered malformed JSON: {Error}"
    )]
    public partial void ValidationMalformedJson(string error);
}
