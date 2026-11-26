using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Common.Telemetry;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

/// <summary>
/// Coordinates ambient step timing, OpenTelemetry spans/metrics, structured instrumentation logging,
/// and unified audit trail metadata/persistence for a single submission operation.
/// </summary>
public sealed class SubmissionExecutionScope : IDisposable
{
    private static readonly AsyncLocal<SubmissionExecutionScope?> CurrentScope = new();

    private static readonly ProcessExecutionInstrumentation DefaultInstrumentation =
        new(NullLogger<ProcessExecutionInstrumentation>.Instance);

    private readonly List<SubmissionProcessExecution> _processes;
    private readonly ProcessExecutionInstrumentation _instrumentation;
    private readonly ISubmissionAuditStore? _auditStore;
    private readonly IActorContext? _actorContext;
    private readonly Activity? _rootActivity;
    private readonly SubmissionExecutionScope? _previousScope;

    private SubmissionExecutionScope(
        Guid submissionId,
        string action,
        ProcessExecutionInstrumentation? instrumentation,
        ISubmissionAuditStore? auditStore,
        IActorContext? actorContext,
        int initialCapacity)
    {
        SubmissionId = submissionId;
        Action = action;
        _instrumentation = instrumentation ?? DefaultInstrumentation;
        _auditStore = auditStore;
        _actorContext = actorContext;
        _processes = new List<SubmissionProcessExecution>(initialCapacity);

        _rootActivity = EstateDataCollectionTelemetry.ApplicationActivitySource.StartActivity($"Submission:{action}");
        if (_rootActivity is not null)
        {
            _rootActivity.SetTag("submission.action", action);
            if (submissionId != Guid.Empty)
            {
                _rootActivity.SetTag("submission.id", submissionId.ToString());
            }

            if (actorContext?.IsAuthenticated == true)
            {
                if (!string.IsNullOrWhiteSpace(actorContext.ActorId))
                {
                    _rootActivity.SetTag("actor.id", actorContext.ActorId);
                }

                _rootActivity.SetTag("actor.auth_type", actorContext.AuthenticationType);
                if (!string.IsNullOrWhiteSpace(actorContext.SubscriptionId))
                {
                    _rootActivity.SetTag("actor.subscription_id", actorContext.SubscriptionId);
                }
            }
        }

        _previousScope = CurrentScope.Value;
        CurrentScope.Value = this;
    }

    public static SubmissionExecutionScope? Current => CurrentScope.Value;

    public Guid SubmissionId { get; private set; }
    public string Action { get; }
    public IReadOnlyList<SubmissionProcessExecution> Processes => _processes;

    public bool IsAuditEnabled { get; private set; }
    public string? SchemaName { get; private set; }
    public int? SchemaVersion { get; private set; }
    public string? OutcomeStatus { get; private set; }
    public int? HttpStatus { get; private set; }
    public string? FailureOutcomeStatus { get; private set; }
    public string? Message { get; private set; }
    public IReadOnlyList<ValidationError>? ValidationErrors { get; private set; }
    public SubmissionAuditIdentifier? Identifier { get; private set; }
    public IReadOnlyDictionary<string, string>? Details { get; private set; }

    public static SubmissionExecutionScope Start(
        string action,
        Guid submissionId = default,
        ProcessExecutionInstrumentation? instrumentation = null,
        ISubmissionAuditStore? auditStore = null,
        int initialCapacity = 8,
        IActorContext? actorContext = null)
    {
        return new SubmissionExecutionScope(submissionId, action, instrumentation, auditStore, actorContext, initialCapacity);
    }

    public static Task<Result<T>> StepAsync<T>(
        SubmissionProcessDescriptor descriptor,
        Func<Task<Result<T>>> operation,
        Func<T, ProcessStepDetails>? describeSuccess = null,
        Func<Error, ProcessStepDetails>? describeFailure = null,
        string? engineOverride = null)
    {
        return Current is { } scope
            ? scope.ExecuteAsync(descriptor, operation, describeSuccess, describeFailure, engineOverride)
            : operation();
    }

    public static Task<Result> StepAsync(
        SubmissionProcessDescriptor descriptor,
        Func<Task<Result>> operation,
        Func<ProcessStepDetails>? describeSuccess = null,
        Func<Error, ProcessStepDetails>? describeFailure = null,
        string? engineOverride = null)
    {
        return Current is { } scope
            ? scope.ExecuteAsync(descriptor, operation, describeSuccess, describeFailure, engineOverride)
            : operation();
    }

    public static Result<T> StepResultSync<T>(
        SubmissionProcessDescriptor descriptor,
        Func<Result<T>> operation,
        Func<T, ProcessStepDetails>? describeSuccess = null,
        Func<Error, ProcessStepDetails>? describeFailure = null,
        string? engineOverride = null)
    {
        return Current is { } scope
            ? scope.ExecuteResultSync(descriptor, operation, describeSuccess, describeFailure, engineOverride)
            : operation();
    }

    public static T StepSync<T>(
        SubmissionProcessDescriptor descriptor,
        Func<T> operation,
        Func<T, ProcessStepDetails> describe,
        string? engineOverride = null)
    {
        return Current is { } scope
            ? scope.ExecuteSync(descriptor, operation, describe, engineOverride)
            : operation();
    }

    public void SetSubmissionId(Guid submissionId)
    {
        SubmissionId = submissionId;
        _rootActivity?.SetTag("submission.id", submissionId.ToString());
    }

    public void SetSubmissionContext(SubmissionSession session, SubmissionAuditIdentifier? identifier = null)
    {
        if (SubmissionId == Guid.Empty && session.SubmissionId != Guid.Empty)
        {
            SetSubmissionId(session.SubmissionId);
        }

        SchemaName = session.SchemaName;
        SchemaVersion = session.SchemaVersion;
        if (identifier is not null)
        {
            Identifier = identifier;
        }

        IsAuditEnabled = true;
    }

    public void SetAuditContext(
        SubmissionAuditIdentifier? identifier = null,
        IReadOnlyList<ValidationError>? validationErrors = null,
        string? message = null,
        string? failureOutcomeStatus = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (identifier is not null)
        {
            Identifier = identifier;
        }

        if (validationErrors is not null)
        {
            ValidationErrors = validationErrors;
        }

        if (message is not null)
        {
            Message = message;
        }

        if (failureOutcomeStatus is not null)
        {
            FailureOutcomeStatus = failureOutcomeStatus;
        }

        if (details is not null)
        {
            Details = details;
        }
    }

    public void SetAuditOutcome(
        string outcomeStatus,
        int httpStatus = 200,
        string? message = null,
        IReadOnlyList<ValidationError>? validationErrors = null,
        SubmissionAuditIdentifier? identifier = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        OutcomeStatus = outcomeStatus;
        HttpStatus = httpStatus;
        Message = message;
        if (validationErrors is not null)
        {
            ValidationErrors = validationErrors;
        }

        if (identifier is not null)
        {
            Identifier = identifier;
        }

        if (details is not null)
        {
            Details = details;
        }
    }

    public void MarkRootFailed(Error error)
    {
        if (!string.IsNullOrWhiteSpace(error.Code))
        {
            _rootActivity?.SetTag("error.code", error.Code);
        }

        _rootActivity?.SetStatus(ActivityStatusCode.Error, error.Description ?? error.Code);
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

        var errorCode = details.ErrorCode ?? (result.IsFailure ? result.Error.Code : null);
        var errors = details.Errors ?? (result.IsFailure ? MapErrorToIssues(result.Error) : null);

        RecordStep(descriptor, details, durationMs, engineOverride, activity, errorCode, errors);
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

        var errorCode = details.ErrorCode ?? (result.IsFailure ? result.Error.Code : null);
        var errors = details.Errors ?? (result.IsFailure ? MapErrorToIssues(result.Error) : null);

        RecordStep(descriptor, details, durationMs, engineOverride, activity, errorCode, errors);
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

        var errorCode = details.ErrorCode ?? (result.IsFailure ? result.Error.Code : null);
        var errors = details.Errors ?? (result.IsFailure ? MapErrorToIssues(result.Error) : null);

        RecordStep(descriptor, details, durationMs, engineOverride, activity, errorCode, errors);
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

        RecordStep(descriptor, details, durationMs, engineOverride, activity, details.ErrorCode, details.Errors);
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
            errorCode: null,
            errors: null);
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

        var resolvedActor = !string.IsNullOrWhiteSpace(actor)
            ? actor
            : _actorContext?.IsAuthenticated == true
                ? _actorContext.AuditActorDisplay
                : null;

        var enrichedDetails = EnrichDetailsWithActor(details, _actorContext);

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
            details: enrichedDetails,
            actor: resolvedActor,
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
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = _previousScope;
        }

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
        string? errorCode,
        IReadOnlyList<SubmissionAuditValidationIssue>? errors)
    {
        var resolvedEngine = details.EngineOverride ?? engineOverride ?? descriptor.Engine;
        var execution = new SubmissionProcessExecution
        {
            ProcessName = descriptor.ProcessName,
            Engine = resolvedEngine,
            Outcome = details.Outcome,
            DurationMs = durationMs,
            ItemCount = details.ItemCount,
            Summary = details.Summary,
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode,
            Errors = errors is { Count: > 0 } ? errors : null
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

    private static IReadOnlyDictionary<string, string>? EnrichDetailsWithActor(
        IReadOnlyDictionary<string, string>? existingDetails,
        IActorContext? actorContext)
    {
        if (actorContext is not { IsAuthenticated: true })
        {
            return existingDetails;
        }

        var merged = existingDetails is not null
            ? new Dictionary<string, string>(existingDetails, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        merged.TryAdd("actor.authType", actorContext.AuthenticationType);

        if (!string.IsNullOrWhiteSpace(actorContext.ActorId))
        {
            merged.TryAdd("actor.id", actorContext.ActorId);
        }

        if (!string.IsNullOrWhiteSpace(actorContext.ActorName))
        {
            merged.TryAdd("actor.name", actorContext.ActorName);
        }

        if (!string.IsNullOrWhiteSpace(actorContext.SubscriptionId))
        {
            merged.TryAdd("actor.subscriptionId", actorContext.SubscriptionId);
        }

        if (!string.IsNullOrWhiteSpace(actorContext.SubscriptionName))
        {
            merged.TryAdd("actor.subscriptionName", actorContext.SubscriptionName);
        }

        foreach (var (key, value) in actorContext.Metadata)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                merged.TryAdd($"actor.meta.{key}", value);
            }
        }

        return merged;
    }

    private static IReadOnlyList<SubmissionAuditValidationIssue>? MapErrorToIssues(Error error)
    {
        if (error.ValidationErrors is { Count: > 0 } validationErrors)
        {
            return validationErrors
                .Select(e => new SubmissionAuditValidationIssue(e.PropertyPath, e.Message, e.RuleId))
                .ToArray();
        }

        var message = !string.IsNullOrWhiteSpace(error.Description) ? error.Description : error.Code;
        return string.IsNullOrWhiteSpace(message)
            ? null
            : [new SubmissionAuditValidationIssue(string.Empty, message, error.Code)];
    }
}
