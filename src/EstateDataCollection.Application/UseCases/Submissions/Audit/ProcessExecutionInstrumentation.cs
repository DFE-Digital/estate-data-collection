using System.Diagnostics;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Telemetry;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

/// <summary>
/// Source-generated instrumentation and OpenTelemetry metric emitter for submission pipeline process steps.
/// </summary>
public sealed partial class ProcessExecutionInstrumentation(
    ILogger<ProcessExecutionInstrumentation> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Submission {SubmissionId} [{Action}] step '{ProcessName}' ({Engine}) succeeded in {DurationMs} ms: {Summary}"
    )]
    public partial void ProcessSucceeded(
        Guid submissionId,
        string action,
        string processName,
        string? engine,
        long durationMs,
        string? summary);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Submission {SubmissionId} [{Action}] step '{ProcessName}' ({Engine}) found {ItemCount} validation issue(s) in {DurationMs} ms: {Summary}"
    )]
    public partial void ProcessValidationErrorsFound(
        Guid submissionId,
        string action,
        string processName,
        string? engine,
        int? itemCount,
        long durationMs,
        string? summary);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Submission {SubmissionId} [{Action}] step '{ProcessName}' ({Engine}) skipped: {Summary}"
    )]
    public partial void ProcessSkipped(
        Guid submissionId,
        string action,
        string processName,
        string? engine,
        string? summary);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Submission {SubmissionId} [{Action}] step '{ProcessName}' ({Engine}) reported missing in {DurationMs} ms: {Summary}"
    )]
    public partial void ProcessMissing(
        Guid submissionId,
        string action,
        string processName,
        string? engine,
        long durationMs,
        string? summary);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Submission {SubmissionId} [{Action}] step '{ProcessName}' ({Engine}) failed in {DurationMs} ms: {Summary}"
    )]
    public partial void ProcessFailed(
        Guid submissionId,
        string action,
        string processName,
        string? engine,
        long durationMs,
        string? summary);

    public void RecordProcess(Guid submissionId, string action, SubmissionProcessExecution execution)
    {
        switch (execution.Outcome)
        {
            case SubmissionProcessOutcome.Succeeded:
                ProcessSucceeded(submissionId, action, execution.ProcessName, execution.Engine, execution.DurationMs, execution.Summary);
                break;
            case SubmissionProcessOutcome.ValidationErrorsFound:
                ProcessValidationErrorsFound(submissionId, action, execution.ProcessName, execution.Engine, execution.ItemCount, execution.DurationMs, execution.Summary);
                break;
            case SubmissionProcessOutcome.Skipped:
                ProcessSkipped(submissionId, action, execution.ProcessName, execution.Engine, execution.Summary);
                break;
            case SubmissionProcessOutcome.Missing:
                ProcessMissing(submissionId, action, execution.ProcessName, execution.Engine, execution.DurationMs, execution.Summary);
                break;
            case SubmissionProcessOutcome.Failed:
                ProcessFailed(submissionId, action, execution.ProcessName, execution.Engine, execution.DurationMs, execution.Summary);
                break;
        }

        var tags = new TagList
        {
            { "submission.action", action },
            { "process.name", execution.ProcessName },
            { "process.engine", execution.Engine ?? string.Empty },
            { "process.outcome", execution.Outcome.ToString() }
        };

        EstateDataCollectionTelemetry.ProcessExecutions.Add(1, tags);
        EstateDataCollectionTelemetry.ProcessDurationMs.Record(execution.DurationMs, tags);
    }

    public void RecordAuditAction(string action, string outcomeStatus, int httpStatus)
    {
        var tags = new TagList
        {
            { "submission.action", action },
            { "outcome.status", outcomeStatus },
            { "http.status", httpStatus }
        };

        EstateDataCollectionTelemetry.SubmissionAuditEvents.Add(1, tags);
    }
}
