using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;

public sealed partial class GetSubmissionStatusUseCaseInstrumentation(
    ILogger<GetSubmissionStatusUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Checking submission status for session {SubmissionId}"
    )]
    public partial void CheckingSubmissionStatus(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Submission session {SubmissionId} not found during status check"
    )]
    public partial void SubmissionNotFound(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Submission {SubmissionId} status check completed: State={Status}, ReadyToFinalise={IsReadyToFinalise}, PendingErrorCount={PendingErrorCount}"
    )]
    public partial void SubmissionStatusChecked(
        Guid submissionId,
        string status,
        bool isReadyToFinalise,
        int pendingErrorCount);
}
