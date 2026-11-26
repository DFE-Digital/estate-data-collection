namespace EstateDataCollection.Application.Models;

public sealed record BatchRuleViolation(
    string TargetPath,
    string? Message = null);