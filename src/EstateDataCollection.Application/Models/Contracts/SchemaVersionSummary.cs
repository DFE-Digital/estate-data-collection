using EstateDataCollection.Common;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Summary of a single schema version in the catalogue.
/// </summary>
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
    IReadOnlyList<string>? LastTestRunWarnings = null,
    bool HasErrorReport = false,
    string? ErrorSummary = null,
    string? SignedOffBy = null,
    DateTimeOffset? SignedOffAtUtc = null,
    DateTimeOffset? DeletedAtUtc = null
);
