using EstateDataCollection.Common;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Summary of a single schema version in the catalogue.
/// </summary>
/// <param name="SchemaName">Unique schema identifier.</param>
/// <param name="Version">Monotonic integer version number.</param>
/// <param name="State">Lifecycle state of this schema version (e.g. `Active`, `Scheduled`, `Retired`).</param>
/// <param name="EffectiveFromUtc">Optional UTC timestamp from which the schema becomes active.</param>
/// <param name="EffectiveToUtc">Optional UTC timestamp after which the schema stops accepting traffic.</param>
/// <param name="IsActiveNow">True when the version is currently within its active lifecycle window.</param>
/// <param name="CanAcceptTraffic">True when public callers may create or finalise submissions against this version.</param>
/// <param name="Sha256Hash">SHA-256 digest of the canonical schema definition.</param>
/// <param name="CreatedAtUtc">UTC timestamp when this schema version was registered.</param>
/// <param name="IdentifierConfig">Establishment identifier extraction configuration for this schema version.</param>
/// <param name="LastTestRunAtUtc">UTC timestamp of the most recent schema regression test run.</param>
/// <param name="LastTestRunAppVersion">Application version that executed the most recent regression test run.</param>
/// <param name="LastTestRunPassed">Outcome of the most recent regression test run.</param>
/// <param name="LastTestRunWarnings">Non-fatal warnings recorded during the most recent regression test run.</param>
public sealed record SchemaVersionSummary(
    string SchemaName,
    int Version,
    Node<int> State,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool IsActiveNow,
    bool CanAcceptTraffic,
    string Sha256Hash,
    DateTimeOffset CreatedAtUtc,
    SchemaIdentifierConfig? IdentifierConfig = null,
    DateTimeOffset? LastTestRunAtUtc = null,
    string? LastTestRunAppVersion = null,
    bool? LastTestRunPassed = null,
    IReadOnlyList<string>? LastTestRunWarnings = null
);
