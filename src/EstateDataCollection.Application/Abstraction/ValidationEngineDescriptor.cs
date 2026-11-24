namespace EstateDataCollection.Application.Abstraction;

/// <summary>
/// Declarative self-description emitted by an isolated validation engine so callers,
/// telemetry, and admin tooling can inspect active engines without runtime reflection.
/// </summary>
public sealed record ValidationEngineDescriptor(
    string EngineId,
    string DisplayName,
    string Kind,
    string Description);
