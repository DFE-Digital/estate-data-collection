namespace EstateDataCollection.Domain.Schema;

/// <summary>
/// Persisted summary of an individual validation engine's execution during a schema regression test run.
/// </summary>
public sealed record SchemaEngineTraceRecord(
    string EngineId,
    string DisplayName,
    string Kind,
    string Description,
    string Outcome,
    int ItemsEvaluated,
    int ViolationCount,
    long DurationMs,
    string? Summary = null);
