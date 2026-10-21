using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Submissions.AutoSubmission;

public sealed partial class AutoSubmissionUseCaseInstrumentation(
    ILogger<AutoSubmissionUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Processing auto-submission for schema {SchemaName} (version: {Version})"
    )]
    public partial void ProcessingAutoSubmission(string schemaName, int? version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Auto-submission rejected: schema {SchemaName} v{Version} is retired"
    )]
    public partial void SchemaRetired(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Auto-submission contract violation on {SchemaName} v{Version} with {ErrorCount} errors"
    )]
    public partial void ContractViolation(string schemaName, int version, int errorCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Auto-submission rejected: invalid URN {Urn}"
    )]
    public partial void InvalidUrn(int urn);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Auto-submission {SubmissionId} for URN {Urn} saved under {SchemaName} v{SchemaVersion} with hash {PayloadHash}"
    )]
    public partial void SubmissionSaved(Guid submissionId, int urn, string schemaName, int schemaVersion, string payloadHash);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to save auto-submission for {SchemaName}: {Error}"
    )]
    public partial void SubmissionSaveFailed(string schemaName, string error);
}
