namespace EstateDataCollection.Common.Results;

/// <summary>
/// Individual field-level or rule-level validation error produced during draft or finalise validation.
/// </summary>
/// <param name="PropertyPath">JSON Pointer or property path identifying the failing field (e.g. `/school/urn`).</param>
/// <param name="Message">Human-readable explanation of the validation failure.</param>
/// <param name="RuleId">Optional rule identifier when the failure originated from a business/quality rule.</param>
public sealed record ValidationError(string PropertyPath, string Message, string? RuleId = null);
