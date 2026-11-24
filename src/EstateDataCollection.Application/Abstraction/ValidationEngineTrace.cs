namespace EstateDataCollection.Application.Abstraction;

/// <summary>
/// Typed execution summary produced per validation engine for a single payload evaluation.
/// </summary>
public sealed record ValidationEngineTrace(
    ValidationEngineDescriptor Descriptor,
    string Outcome,
    int ItemsEvaluated,
    int ViolationCount,
    long DurationMs,
    string? Summary = null);