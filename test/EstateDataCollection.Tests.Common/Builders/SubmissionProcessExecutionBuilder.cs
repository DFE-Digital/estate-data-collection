using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SubmissionProcessExecutionBuilder
{
    private string _processName = SubmissionProcessCatalog.SchemaResolution.ProcessName;
    private string? _engine = SubmissionProcessCatalog.SchemaResolution.Engine;
    private SubmissionProcessOutcome _outcome = SubmissionProcessOutcome.Succeeded;
    private long _durationMs = 1;
    private int? _itemCount;
    private string? _summary;
    private string? _errorCode;
    private readonly List<SubmissionAuditValidationIssue> _errors = [];

    public SubmissionProcessExecutionBuilder For(SubmissionProcessDescriptor descriptor)
    {
        _processName = descriptor.ProcessName;
        _engine = descriptor.Engine;
        return this;
    }

    public SubmissionProcessExecutionBuilder WithProcessName(string processName)
    {
        _processName = processName;
        return this;
    }

    public SubmissionProcessExecutionBuilder WithEngine(string? engine)
    {
        _engine = engine;
        return this;
    }

    public SubmissionProcessExecutionBuilder WithOutcome(SubmissionProcessOutcome outcome)
    {
        _outcome = outcome;
        return this;
    }

    public SubmissionProcessExecutionBuilder WithDurationMs(long durationMs)
    {
        _durationMs = durationMs;
        return this;
    }

    public SubmissionProcessExecutionBuilder WithItemCount(int? itemCount)
    {
        _itemCount = itemCount;
        return this;
    }

    public SubmissionProcessExecutionBuilder WithSummary(string? summary)
    {
        _summary = summary;
        return this;
    }

    public SubmissionProcessExecutionBuilder WithErrorCode(string? errorCode)
    {
        _errorCode = errorCode;
        return this;
    }

    public SubmissionProcessExecutionBuilder WithError(string propertyPath, string message, string? ruleId = null)
    {
        _errors.Add(new SubmissionAuditValidationIssue(propertyPath, message, ruleId));
        return this;
    }

    public SubmissionProcessExecutionBuilder WithErrors(IEnumerable<SubmissionAuditValidationIssue> errors)
    {
        _errors.AddRange(errors);
        return this;
    }

    public SubmissionProcessExecution Build()
    {
        return new SubmissionProcessExecution
        {
            ProcessName = _processName,
            Engine = _engine,
            Outcome = _outcome,
            DurationMs = _durationMs,
            ItemCount = _itemCount,
            Summary = _summary,
            ErrorCode = _errorCode,
            Errors = _errors.Count > 0 ? _errors.ToArray() : null
        };
    }
}
