using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Domain.Schema;

/// <summary>
/// Diagnostic report persisted to <c>{schemaName}/v{version}.error.json</c> when a schema fails hard checks
/// (<see cref="SchemaLifecycleState.Invalid"/>) or completes with warnings (<see cref="SchemaLifecycleState.Unvalidated"/>).
/// Preserved alongside <c>.meta.json</c> even if the schema contract is deleted.
/// </summary>
public sealed record SchemaErrorReport(
    string SchemaName,
    int Version,
    DateTimeOffset RecordedAtUtc,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null,
    IReadOnlyList<string>? SyntaxErrors = null,
    IReadOnlyList<string>? FixtureErrors = null,
    IReadOnlyList<string>? Warnings = null,
    string? Summary = null);
