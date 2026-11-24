using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Domain.Schema;

/// <summary>
/// Captures the detailed verification outcome for a single stored or candidate invalid fixture,
/// including all triggered schema / rule / identifier errors, whether optional expected error checks matched,
/// and any soft warning when validation failed without configured expectedErrors assertions.
/// </summary>
public sealed record InvalidFixtureOutcome(
    int Index,
    bool FailedAsExpected,
    IReadOnlyList<ValidationMessage> TriggeredErrors,
    IReadOnlyList<string>? ExpectedErrors = null,
    IReadOnlyList<string>? UnmatchedExpectedErrors = null,
    string? Description = null,
    string? Warning = null);
