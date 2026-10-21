using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.AzureFunctions.Routes;

public sealed partial class SchemaFunctionsInstrumentation(
    ILogger<SchemaFunctions> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP trigger: VerifySchema invoked"
    )]
    public partial void VerifySchemaInvoked();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP trigger: SubmitSchema invoked for {SchemaName} v{Version}"
    )]
    public partial void SubmitSchemaInvoked(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HTTP trigger {FunctionName} received empty or invalid request body"
    )]
    public partial void EmptyRequestBodyReceived(string functionName);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP trigger {FunctionName} completed with status code {StatusCode}"
    )]
    public partial void EndpointCompleted(string functionName, int statusCode);
}
