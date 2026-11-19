using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

internal static class SubmissionAuditRecorder
{
    public static int MapHttpStatus(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => 422,
        ErrorType.NotFound => 404,
        ErrorType.Conflict => 409,
        ErrorType.Unauthorized => 401,
        ErrorType.Forbidden => 403,
        _ => 500
    };

    public static async Task SafeAppendAsync(
        ISubmissionAuditStore? auditStore,
        Guid submissionId,
        string action,
        string? schemaName,
        int? schemaVersion,
        string outcomeStatus,
        int httpStatus,
        IReadOnlyList<SubmissionProcessExecution> processesConsumed,
        string? errorCode = null,
        string? message = null,
        IReadOnlyList<ValidationError>? validationErrors = null,
        SubmissionAuditIdentifier? identifier = null,
        IReadOnlyDictionary<string, string>? details = null,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        if (auditStore is null)
        {
            return;
        }

        try
        {
            var mappedErrors = validationErrors is { Count: > 0 }
                ? validationErrors
                    .Select(e => new SubmissionAuditValidationIssue(e.PropertyPath, e.Message, e.RuleId))
                    .ToArray()
                : null;

            var entry = new SubmissionAuditEntry
            {
                EventId = Guid.CreateVersion7(),
                TimestampUtc = DateTimeOffset.UtcNow,
                SubmissionId = submissionId,
                Action = action,
                TraceId = Activity.Current?.TraceId.ToString(),
                Actor = actor,
                SchemaName = schemaName,
                SchemaVersion = schemaVersion,
                Outcome = new SubmissionAuditOutcome
                {
                    Status = outcomeStatus,
                    HttpStatus = httpStatus,
                    ErrorCode = errorCode,
                    Message = message,
                    ValidationErrorCount = validationErrors?.Count ?? 0,
                    ValidationErrors = mappedErrors
                },
                Identifier = identifier,
                ProcessesConsumed = processesConsumed,
                Details = details
            };

            await auditStore.AppendAsync(entry, cancellationToken);
        }
        catch
        {
            // Audit trail recording is best-effort and must not fail the primary submission operation.
        }
    }
}
