namespace EstateDataCollection.Common.Results;

public sealed record ValidationError(
    string PropertyPath,
    string Message,
    string? RuleId = null,
    RuleSeverity Severity = RuleSeverity.Error,
    string? Action = null,
    string? EngineId = null);
