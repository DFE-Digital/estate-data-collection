using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Common.Web.Endpoints;

public sealed partial class FunctionEndpointInstrumentation(
    ILogger<FunctionEndpointInstrumentation> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP endpoint {EndpointName} invoked ({Context})"
    )]
    public partial void EndpointInvoked(string endpointName, string context);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HTTP endpoint {EndpointName} request binding failed ({ErrorCode}: {Reason})"
    )]
    public partial void EndpointBindingFailed(string endpointName, string errorCode, string reason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP endpoint {EndpointName} completed with status code {StatusCode}"
    )]
    public partial void EndpointCompleted(string endpointName, int statusCode);
}
