using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public sealed partial class UpdateSubmissionUseCaseInstrumentation(
    ILogger<UpdateSubmissionUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Updating draft payload for submission session {SubmissionId}"
    )]
    public partial void UpdatingDraftPayload(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Submission session {SubmissionId} not found"
    )]
    public partial void SubmissionNotFound(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Attempted to update already finalised submission {SubmissionId}"
    )]
    public partial void SubmissionAlreadyFinalised(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Draft payload for submission {SubmissionId} failed validation with {ErrorCount} errors"
    )]
    public partial void DraftValidationFailed(Guid submissionId, int errorCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Draft payload for submission {SubmissionId} saved. IsValid: {IsValid}, PendingErrors: {PendingErrorCount}"
    )]
    public partial void DraftPayloadSaved(Guid submissionId, bool isValid, int pendingErrorCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to save draft payload for submission {SubmissionId}: {Error}"
    )]
    public partial void DraftPayloadSaveFailed(Guid submissionId, string error);
}
