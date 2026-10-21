using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.AzureFunctions.Routes;

public sealed partial class SubmissionFunctionsInstrumentation(
    ILogger<SubmissionFunctions> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP trigger: CreateSubmission invoked for {SchemeName} (version: {Version})"
    )]
    public partial void CreateSubmissionInvoked(string schemeName, int? version);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP trigger: UpdateSubmission invoked for {SubmissionId}"
    )]
    public partial void UpdateSubmissionInvoked(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP trigger: FinaliseSubmission invoked for {SubmissionId}"
    )]
    public partial void FinaliseSubmissionInvoked(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HTTP trigger {FunctionName} received empty request payload for submission {SubmissionId}"
    )]
    public partial void EmptyPayloadReceived(string functionName, Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP trigger {FunctionName} completed with status code {StatusCode}"
    )]
    public partial void EndpointCompleted(string functionName, int statusCode);
}
