using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Full details of a specific schema version including extracted field definitions, quality rules, and stored test fixtures.
/// </summary>
/// <param name="SchemaName">Unique schema identifier.</param>
/// <param name="Version">Monotonic integer version number.</param>
/// <param name="State">Lifecycle state of this schema version.</param>
/// <param name="EffectiveFromUtc">Optional UTC timestamp from which the schema becomes active.</param>
/// <param name="EffectiveToUtc">Optional UTC timestamp after which the schema stops accepting traffic.</param>
/// <param name="IsActiveNow">True when the version is currently active.</param>
/// <param name="CanAcceptTraffic">True when public callers may submit payloads against this version.</param>
/// <param name="Sha256Hash">SHA-256 digest of the canonical schema definition.</param>
/// <param name="CreatedAtUtc">UTC timestamp when this schema version was registered.</param>
/// <param name="Fields">Flattened or hierarchical field descriptors extracted from the JSON Schema contract.</param>
/// <param name="QualityRules">Data quality and business rules evaluated alongside JSON Schema validation.</param>
/// <param name="RawJsonSchema">Raw JSON Schema document when `includeRawJsonSchema=true` is requested.</param>
/// <param name="IdentifierConfig">Establishment identifier extraction configuration.</param>
/// <param name="ValidFixtures">Sample valid fixture payloads stored with the schema version.</param>
/// <param name="InvalidFixtures">Sample invalid fixture payloads stored with the schema version.</param>
/// <param name="LastTestRunAtUtc">UTC timestamp of the most recent regression test run.</param>
/// <param name="LastTestRunAppVersion">Application version that ran the most recent regression test suite.</param>
/// <param name="LastTestRunPassed">Outcome of the most recent regression test run.</param>
/// <param name="LastTestRunErrors">Failure messages from the most recent regression test run, if any.</param>
/// <param name="LastTestRunInvalidFixtures">Per-fixture verification outcomes from the most recent regression test run.</param>
/// <param name="LastTestRunWarnings">Non-fatal warnings from the most recent regression test run.</param>
/// <param name="LastTestRunEngineTraces">Per-engine execution telemetry captured during the last test run.</param>
/// <param name="RegisteredEngines">Descriptors for the validation engines registered in this host.</param>
public sealed record SchemaVersionDetail(
    string SchemaName,
    int Version,
    Node<int> State,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool IsActiveNow,
    bool CanAcceptTraffic,
    string Sha256Hash,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<SchemaFieldDescriptor> Fields,
    IReadOnlyList<SchemaQualityRuleSummary> QualityRules,
    string? RawJsonSchema = null,
    SchemaIdentifierConfig? IdentifierConfig = null,
    IReadOnlyList<string>? ValidFixtures = null,
    IReadOnlyList<string>? InvalidFixtures = null,
    DateTimeOffset? LastTestRunAtUtc = null,
    string? LastTestRunAppVersion = null,
    bool? LastTestRunPassed = null,
    IReadOnlyList<string>? LastTestRunErrors = null,
    IReadOnlyList<InvalidFixtureOutcome>? LastTestRunInvalidFixtures = null,
    IReadOnlyList<string>? LastTestRunWarnings = null,
    IReadOnlyList<ValidationEngineTrace>? LastTestRunEngineTraces = null,
    IReadOnlyList<ValidationEngineDescriptor>? RegisteredEngines = null
);
