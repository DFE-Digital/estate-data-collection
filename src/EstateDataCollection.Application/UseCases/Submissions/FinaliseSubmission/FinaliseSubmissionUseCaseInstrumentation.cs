using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public sealed partial class FinaliseSubmissionUseCaseInstrumentation(
    ILogger<FinaliseSubmissionUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Attempting to finalise submission session {SubmissionId}"
    )]
    public partial void FinalisingSubmission(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Cannot finalise: submission session {SubmissionId} not found"
    )]
    public partial void SubmissionNotFound(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Submission session {SubmissionId} has already been finalised"
    )]
    public partial void SubmissionAlreadyFinalised(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Draft payload missing or unreadable for submission session {SubmissionId}"
    )]
    public partial void DraftPayloadMissing(Guid submissionId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Finalisation validation failed for submission {SubmissionId} with {ErrorCount} errors"
    )]
    public partial void ValidationFailed(Guid submissionId, int errorCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Submission {SubmissionId} has invalid URN {Urn}; cannot promote to CDE"
    )]
    public partial void InvalidUrn(Guid submissionId, int urn);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Submission {SubmissionId} (URN: {Urn}) finalised and promoted to processed storage at {TargetUri} under {SchemaName} v{SchemaVersion}"
    )]
    public partial void SubmissionFinalisedAndPromoted(Guid submissionId, int urn, string schemaName, int schemaVersion, string targetUri);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to promote submission {SubmissionId} to processed storage: {Error}"
    )]
    public partial void SubmissionPromotionFailed(Guid submissionId, string error);
}
