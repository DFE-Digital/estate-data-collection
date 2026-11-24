namespace EstateDataCollection.Common.Results;

public sealed record ValidationError(string PropertyPath, string Message, string? RuleId = null);
