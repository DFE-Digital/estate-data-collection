using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Telemetry;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

/// <summary>
/// Coordinates step timing, OpenTelemetry spans/metrics, structured instrumentation logging,
/// and audit trail persistence for a single submission operation.
/// </summary>
public sealed class SubmissionExecutionScope : IDisposable
{
    private static readonly ProcessExecutionInstrumentation DefaultInstrumentation =
        new(NullLogger<ProcessExecutionInstrumentation>.Instance);

    private readonly List<SubmissionProcessExecution> _processes;
    private readonly ProcessExecutionInstrumentation _instrumentation;
    private readonly ISubmissionAuditStore? _auditStore;
    private readonly Activity? _rootActivity;

    private SubmissionExecutionScope(
        Guid submissionId,
        string action,
        ProcessExecutionInstrumentation? instrumentation,
        ISubmissionAuditStore? auditStore,
        int initialCapacity)
    {
        SubmissionId = submissionId;
        Action = action;
        _instrumentation = instrumentation ?? DefaultInstrumentation;
        _auditStore = auditStore;
        _processes = new List<SubmissionProcessExecution>(initialCapacity);

        _rootActivity = EstateDataCollectionTelemetry.ApplicationActivitySource.StartActivity($"Submission:{action}");
        if (_rootActivity is not null)
        {
            _rootActivity.SetTag("submission.action", action);
            if (submissionId != Guid.Empty)
            {
                _rootActivity.SetTag("submission.id", submissionId.ToString());
            }
        }
    }

    public Guid SubmissionId { get; private set; }
    public string Action { get; }
    public IReadOnlyList<SubmissionProcessExecution> Processes => _processes;

    public static SubmissionExecutionScope Start(
        string action,
        Guid submissionId = default,
        ProcessExecutionInstrumentation? instrumentation = null,
        ISubmissionAuditStore? auditStore = null,
        int initialCapacity = 8)
    {
        return new SubmissionExecutionScope(submissionId, action, instrumentation, auditStore, initialCapacity);
    }

    public void SetSubmissionId(Guid submissionId)
    {
        SubmissionId = submissionId;
        _rootActivity?.SetTag("submission.id", submissionId.ToString());
    }

    public async Task<Result<T>> ExecuteAsync<T>(
        SubmissionProcessDescriptor descriptor,
        Func<Task<Result<T>>> operation,
        Func<T, ProcessStepDetails>? describeSuccess = null,
        Func<Error, ProcessStepDetails>? describeFailure = null,
        string? engineOverride = null)
    {
        using var activity = StartStepActivity(descriptor, engineOverride);
        var startTimestamp = Stopwatch.GetTimestamp();

        var result = await operation();

        var durationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        var details = result.IsSuccess
            ? describeSuccess?.Invoke(result.Value) ?? new ProcessStepDetails()
            : describeFailure?.Invoke(result.Error) ?? new ProcessStepDetails(
                Outcome: SubmissionProcessOutcome.Failed,
                Summary: result.Error.Description ?? result.Error.Code);

        RecordStep(descriptor, details, durationMs, engineOverride, activity, result.IsFailure ? result.Error.Code : null);
        return result;
    }

    public async Task<Result> ExecuteAsync(
        SubmissionProcessDescriptor descriptor,
        Func<Task<Result>> operation,
        Func<ProcessStepDetails>? describeSuccess = null,
        Func<Error, ProcessStepDetails>? describeFailure = null,
        string? engineOverride = null)
    {
        using var activity = StartStepActivity(descriptor, engineOverride);
        var startTimestamp = Stopwatch.GetTimestamp();

        var result = await operation();

        var durationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        var details = result.IsSuccess
            ? describeSuccess?.Invoke() ?? new ProcessStepDetails()
            : describeFailure?.Invoke(result.Error) ?? new ProcessStepDetails(
                Outcome: SubmissionProcessOutcome.Failed,
                Summary: result.Error.Description ?? result.Error.Code);

        RecordStep(descriptor, details, durationMs, engineOverride, activity, result.IsFailure ? result.Error.Code : null);
        return result;
    }

    public Result<T> ExecuteResultSync<T>(
        SubmissionProcessDescriptor descriptor,
        Func<Result<T>> operation,
        Func<T, ProcessStepDetails>? describeSuccess = null,
        Func<Error, ProcessStepDetails>? describeFailure = null,
        string? engineOverride = null)
    {
        using var activity = StartStepActivity(descriptor, engineOverride);
        var startTimestamp = Stopwatch.GetTimestamp();

        var result = operation();

        var durationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        var details = result.IsSuccess
            ? describeSuccess?.Invoke(result.Value) ?? new ProcessStepDetails()
            : describeFailure?.Invoke(result.Error) ?? new ProcessStepDetails(
                Outcome: SubmissionProcessOutcome.Failed,
                Summary: result.Error.Description ?? result.Error.Code);

        RecordStep(descriptor, details, durationMs, engineOverride, activity, result.IsFailure ? result.Error.Code : null);
        return result;
    }

    public T ExecuteSync<T>(
        SubmissionProcessDescriptor descriptor,
        Func<T> operation,
        Func<T, ProcessStepDetails> describe,
        string? engineOverride = null)
    {
        using var activity = StartStepActivity(descriptor, engineOverride);
        var startTimestamp = Stopwatch.GetTimestamp();

        var value = operation();

        var durationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        var details = describe(value);

        RecordStep(descriptor, details, durationMs, engineOverride, activity, errorCode: null);
        return value;
    }

    public void RecordSkipped(
        SubmissionProcessDescriptor descriptor,
        int? itemCount = null,
        string? summary = null,
        string? engineOverride = null)
    {
        RecordStep(
            descriptor,
            new ProcessStepDetails(SubmissionProcessOutcome.Skipped, itemCount, summary, engineOverride),
            durationMs: 0,
            engineOverride,
            activity: null,
            errorCode: null);
    }

    public void ImportProcesses(IEnumerable<SubmissionProcessExecution> processes)
    {
        foreach (var process in processes)
        {
            _processes.Add(process);
            _instrumentation.RecordProcess(SubmissionId, Action, process);
        }
    }

    public async Task RecordAuditAsync(
        string? schemaName,
        int? schemaVersion,
        string outcomeStatus,
        int httpStatus,
        string? errorCode = null,
        string? message = null,
        IReadOnlyList<ValidationError>? validationErrors = null,
        SubmissionAuditIdentifier? identifier = null,
        IReadOnlyDictionary<string, string>? details = null,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        _rootActivity?.SetTag("outcome.status", outcomeStatus);
        _rootActivity?.SetTag("http.status", httpStatus);
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            _rootActivity?.SetTag("error.code", errorCode);
        }

        _instrumentation.RecordAuditAction(Action, outcomeStatus, httpStatus);

        await SubmissionAuditRecorder.SafeAppendAsync(
            _auditStore,
            SubmissionId,
            Action,
            schemaName,
            schemaVersion,
            outcomeStatus,
            httpStatus,
            _processes.ToArray(),
            errorCode: errorCode,
            message: message,
            validationErrors: validationErrors,
            identifier: identifier,
            details: details,
            actor: actor,
            cancellationToken: cancellationToken);
    }

    public Task RecordErrorAuditAsync(
        string? schemaName,
        int? schemaVersion,
        Error error,
        string? outcomeStatus = null,
        string? message = null,
        IReadOnlyList<ValidationError>? validationErrors = null,
        SubmissionAuditIdentifier? identifier = null,
        IReadOnlyDictionary<string, string>? details = null,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedStatus = outcomeStatus ?? (error.Type is ErrorType.Validation or ErrorType.Forbidden or ErrorType.Conflict
            ? "Rejected"
            : "Failed");

        _rootActivity?.SetStatus(ActivityStatusCode.Error, message ?? error.Description ?? error.Code);

        return RecordAuditAsync(
            schemaName,
            schemaVersion,
            resolvedStatus,
            SubmissionAuditRecorder.MapHttpStatus(error.Type),
            errorCode: error.Code,
            message: message ?? error.Description,
            validationErrors: validationErrors,
            identifier: identifier,
            details: details,
            actor: actor,
            cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        _rootActivity?.Dispose();
    }

    private Activity? StartStepActivity(SubmissionProcessDescriptor descriptor, string? engineOverride)
    {
        var activity = EstateDataCollectionTelemetry.ApplicationActivitySource.StartActivity(descriptor.ProcessName);
        if (activity is not null)
        {
            activity.SetTag("process.name", descriptor.ProcessName);
            activity.SetTag("process.engine", engineOverride ?? descriptor.Engine);
            if (SubmissionId != Guid.Empty)
            {
                activity.SetTag("submission.id", SubmissionId.ToString());
            }
        }

        return activity;
    }

    private void RecordStep(
        SubmissionProcessDescriptor descriptor,
        ProcessStepDetails details,
        long durationMs,
        string? engineOverride,
        Activity? activity,
        string? errorCode)
    {
        var resolvedEngine = details.EngineOverride ?? engineOverride ?? descriptor.Engine;
        var execution = new SubmissionProcessExecution
        {
            ProcessName = descriptor.ProcessName,
            Engine = resolvedEngine,
            Outcome = details.Outcome,
            DurationMs = durationMs,
            ItemCount = details.ItemCount,
            Summary = details.Summary
        };

        _processes.Add(execution);
        _instrumentation.RecordProcess(SubmissionId, Action, execution);

        if (activity is not null)
        {
            activity.SetTag("process.outcome", details.Outcome.ToString());
            if (details.ItemCount.HasValue)
            {
                activity.SetTag("process.item_count", details.ItemCount.Value);
            }

            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                activity.SetTag("error.code", errorCode);
            }

            if (details.Outcome == SubmissionProcessOutcome.Failed)
            {
                activity.SetStatus(ActivityStatusCode.Error, details.Summary);
            }
        }
    }
}
