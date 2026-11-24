using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Infrastructure.Persistence;

public sealed partial class BlobSubmissionInfrastructureInstrumentation(
    ILogger<BlobSubmissionInfrastructure> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Session blob created for submission {SubmissionId} ({SchemaName} v{SchemaVersion})"
    )]
    public partial void SessionCreated(Guid submissionId, string schemaName, int schemaVersion);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Session blob retrieved for submission {SubmissionId}"
    )]
    public partial void SessionRetrieved(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Session blob not found for submission {SubmissionId}"
    )]
    public partial void SessionNotFound(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Draft payload blob saved for submission {SubmissionId}"
    )]
    public partial void DraftPayloadSaved(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Draft payload retrieved for submission {SubmissionId}"
    )]
    public partial void DraftPayloadRetrieved(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Draft payload blob not found for submission {SubmissionId}"
    )]
    public partial void DraftPayloadNotFound(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Promoted submission {SubmissionId} ({IdentifierType}: {IdentifierValue}) to processed container at {TargetBlobUri}"
    )]
    public partial void PayloadPromotedToProcessed(
        Guid submissionId,
        string? identifierType,
        string? identifierValue,
        string targetBlobUri);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Storage operation {Operation} failed: {Error}"
    )]
    public partial void StorageOperationFailed(string operation, string error);
}
