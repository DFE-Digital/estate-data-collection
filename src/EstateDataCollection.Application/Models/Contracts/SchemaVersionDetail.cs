using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Full details of a specific schema version including extracted field definitions, quality rules, and stored test fixtures.
/// </summary>
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
    IReadOnlyList<ValidationEngineDescriptor>? RegisteredEngines = null,
    SchemaValidationPolicy? ValidationPolicy = null
);
