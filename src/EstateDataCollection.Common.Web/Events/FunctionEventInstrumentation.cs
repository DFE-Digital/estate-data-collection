using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Common.Web.Events;

public sealed partial class FunctionEventInstrumentation(
    ILogger<FunctionEventInstrumentation> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Event handler {EventName} triggered for {TriggerSource}"
    )]
    public partial void EventTriggered(string eventName, string triggerSource);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Event handler {EventName} failed for {TriggerSource}: {ErrorCode} - {Reason}"
    )]
    public partial void EventFailed(string eventName, string triggerSource, string errorCode, string reason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Event handler {EventName} completed for {TriggerSource}"
    )]
    public partial void EventCompleted(string eventName, string triggerSource);
}
