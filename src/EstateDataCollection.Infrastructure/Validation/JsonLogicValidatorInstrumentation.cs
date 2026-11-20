using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Infrastructure.Validation;

public sealed partial class JsonLogicValidatorInstrumentation(
    ILogger<JsonLogicValidator> logger
) : IInstrumentation
{
    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Debug,
        Message = "Starting quality rule validation across {RuleCount} rules.")]
    public partial void ValidationStarted(int ruleCount);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Debug,
        Message = "Quality rule validation passed with zero violations.")]
    public partial void ValidationPassed();

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Warning,
        Message = "Quality rule validation failed with {ViolationCount} violations.")]
    public partial void ValidationFailed(int violationCount);

    [LoggerMessage(
        EventId = 1303,
        Level = LogLevel.Warning,
        Message = "Quality rule '{RuleId}' ({Engine}) violated at '{PropertyPath}': {Message}")]
    public partial void RuleViolation(string ruleId, string engine, string propertyPath, string message);

    [LoggerMessage(
        EventId = 1304,
        Level = LogLevel.Error,
        Message = "Quality rule validation failed due to malformed JSON payload: {ExceptionMessage}")]
    public partial void ValidationMalformedJson(string exceptionMessage);

    [LoggerMessage(
        EventId = 1305,
        Level = LogLevel.Warning,
        Message = "Skipping quality rule '{RuleId}' because engine '{Engine}' is not enabled or supported.")]
    public partial void EngineSkipped(string ruleId, string engine);
}
