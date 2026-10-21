using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;

public sealed partial class CreateSubmissionUseCaseInstrumentation(
    ILogger<CreateSubmissionUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Creating new submission session for schema {SchemaName} (version: {Version})"
    )]
    public partial void CreatingSubmissionSession(string schemaName, int? version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Explicit schema {SchemaName} v{Version} is retired"
    )]
    public partial void SchemaRetired(string schemaName, int version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to resolve schema for {SchemaName}: {Error}"
    )]
    public partial void SchemaResolutionFailed(string schemaName, string error);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Created submission session {SubmissionId} for schema {SchemaName} v{SchemaVersion}"
    )]
    public partial void SubmissionSessionCreated(Guid submissionId, string schemaName, int schemaVersion);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to create submission session for {SchemaName}: {Error}"
    )]
    public partial void SubmissionSessionCreationFailed(string schemaName, string error);
}
