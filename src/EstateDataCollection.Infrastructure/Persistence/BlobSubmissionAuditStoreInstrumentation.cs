using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Infrastructure.Persistence;

public sealed partial class BlobSubmissionAuditStoreInstrumentation(
    ILogger<BlobSubmissionAuditStore> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Audit entry {EventId} appended for submission {SubmissionId} ({Action} -> {OutcomeStatus}) at {BlobName}"
    )]
    public partial void AuditEntryAppended(Guid submissionId, Guid eventId, string action, string outcomeStatus, string blobName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to append audit entry {EventId} for submission {SubmissionId}: {Error}"
    )]
    public partial void AuditAppendFailed(Guid submissionId, Guid eventId, string error);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to read audit trail for submission {SubmissionId}: {Error}"
    )]
    public partial void AuditListFailed(Guid submissionId, string error);
}
