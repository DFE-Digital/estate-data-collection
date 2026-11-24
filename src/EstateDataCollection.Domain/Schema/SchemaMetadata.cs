using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace EstateDataCollection.Domain.Schema;

public class SchemaMetadata
{
    public required string Name { get; init; }
    public required int Version { get; init; }
    public required string RawJsonSchema { get; init; }
    public required string Sha256Hash { get; init; }

    [JsonInclude]
    public SchemaLifecycleState State { get; private set; }

    [JsonInclude]
    public DateTimeOffset? EffectiveFromUtc { get; private set; }

    [JsonInclude]
    public DateTimeOffset? EffectiveToUtc { get; private set; }

    [JsonInclude]
    public SchemaIdentifierConfig? IdentifierConfig { get; private set; }

    [JsonInclude]
    public DateTimeOffset? LastTestRunAtUtc { get; private set; }

    [JsonInclude]
    public string? LastTestRunAppVersion { get; private set; }

    [JsonInclude]
    public bool? LastTestRunPassed { get; private set; }

    [JsonInclude]
    public IReadOnlyList<string>? LastTestRunErrors { get; private set; }

    [JsonInclude]
    public IReadOnlyList<InvalidFixtureOutcome>? LastTestRunInvalidFixtures { get; private set; }

    [JsonInclude]
    public IReadOnlyList<string>? LastTestRunWarnings { get; private set; }

    [JsonInclude]
    public IReadOnlyList<SchemaEngineTraceRecord>? LastTestRunEngineTraces { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    
    #region Domain Factories

    /// <summary>
    /// Factory for creating a brand new schema in Draft state.
    /// Computes hash and enforces core domain invariants.
    /// </summary>
    public static SchemaMetadata CreateDraft(
        string name,
        int version,
        string rawJsonSchema,
        SchemaIdentifierConfig? identifierConfig = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJsonSchema);

        var normalizedConfig = ValidateAndNormalizeIdentifier(identifierConfig);

        return new SchemaMetadata
        {
            Name = name,
            Version = version,
            RawJsonSchema = rawJsonSchema,
            Sha256Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJsonSchema))),
            State = SchemaLifecycleState.Draft,
            IdentifierConfig = normalizedConfig
        };
    }

    #endregion

    #region Capacilities Explicit Invariants

    /// <summary>
    /// Can this schema accept any traffic? (Active versions and Deprecated versions in grace period)
    /// </summary>
    public bool CanAcceptTraffic
    {
        get { return State is SchemaLifecycleState.Active or SchemaLifecycleState.Deprecated; }
    }

    /// <summary>
    /// Is this schema version visible in the public discovery API?
    /// </summary>
    public bool IsPubliclyVisible
    {
        get
        {
            return State is SchemaLifecycleState.Active
                or SchemaLifecycleState.Scheduled
                or SchemaLifecycleState.Deprecated;
        }
    }

    /// <summary>
    /// Is this eligible to be the default version when no explicit version is requested?
    /// </summary>
    public bool CanBeDefault
    {
        get { return State is SchemaLifecycleState.Active; }
    }

    /// <summary>
    /// Can the schema definition still be edited or overwritten?
    /// </summary>
    public bool IsMutable
    {
        get { return State is SchemaLifecycleState.Draft; }
    }

    /// <summary>
    /// Can dry-run test payloads be executed against this schema?
    /// </summary>
    public bool AllowsDryRun
    {
        get { return State is SchemaLifecycleState.Draft or SchemaLifecycleState.Validated; }
    }

    /// <summary>
    /// Can the schema be used
    /// </summary>
    public bool IsRetired
    {
        get { return State is SchemaLifecycleState.Retired; }
    }

    #endregion

    #region Domain Specs

    public bool IsActiveAt(DateTimeOffset timestamp)
    {
        if (State is not (SchemaLifecycleState.Active or SchemaLifecycleState.Scheduled))
        {
            return false;
        }

        var hasStarted = EffectiveFromUtc is not null && timestamp >= EffectiveFromUtc.Value;
        var hasNotEnded = EffectiveToUtc is null || timestamp < EffectiveToUtc.Value;

        return hasStarted && hasNotEnded;
    }

    /// <summary>
    /// Determines whether this schema version can accept new submissions at the specified timestamp
    /// (either currently within its active window or in Deprecated grace period).
    /// </summary>
    public bool CanAcceptTrafficAt(DateTimeOffset timestamp)
    {
        return IsActiveAt(timestamp) || State == SchemaLifecycleState.Deprecated;
    }

    /// <summary>
    /// Resolves the effective lifecycle state at the given timestamp
    /// (transitions a Scheduled schema whose EffectiveFromUtc has arrived into Active).
    /// </summary>
    public SchemaLifecycleState GetEffectiveStateAt(DateTimeOffset timestamp)
    {
        return State == SchemaLifecycleState.Scheduled && IsActiveAt(timestamp)
            ? SchemaLifecycleState.Active
            : State;
    }

    /// <summary>
    /// Marks the schema as structurally validated and promoted to the schemas registry,
    /// without assigning effective dates yet. Effective dates will be assigned later via the Admin API.
    /// </summary>
    public void MarkValidated()
    {
        EffectiveFromUtc = null;
        EffectiveToUtc = null;
        State = SchemaLifecycleState.Validated;
    }

    /// <summary>
    /// Configures or updates the per-schema-version establishment identifier (e.g. URN, UKPRN, LAEstab, custom pointer) metadata.
    /// </summary>
    public void ConfigureIdentifier(SchemaIdentifierConfig? identifierConfig)
    {
        IdentifierConfig = ValidateAndNormalizeIdentifier(identifierConfig);
    }

    /// <summary>
    /// Records the result and app version of executing the stored schema regression tests
    /// (schema syntax, business logic rules, identifier extraction, and valid/invalid fixtures).
    /// </summary>
    public void RecordTestRun(
        bool passed,
        string appVersion,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<InvalidFixtureOutcome>? invalidFixtureOutcomes = null,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<SchemaEngineTraceRecord>? engineTraces = null)
    {
        LastTestRunAtUtc = DateTimeOffset.UtcNow;
        LastTestRunAppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim();
        LastTestRunPassed = passed;
        LastTestRunErrors = errors is { Count: > 0 } ? errors.ToArray() : [];
        LastTestRunInvalidFixtures = invalidFixtureOutcomes is { Count: > 0 }
            ? invalidFixtureOutcomes.ToArray()
            : [];
        LastTestRunWarnings = warnings is { Count: > 0 } ? warnings.ToArray() : [];
        LastTestRunEngineTraces = engineTraces is { Count: > 0 } ? engineTraces.ToArray() : [];
    }

    private static SchemaIdentifierConfig? ValidateAndNormalizeIdentifier(SchemaIdentifierConfig? identifierConfig)
    {
        if (identifierConfig is null)
        {
            return null;
        }

        var validation = identifierConfig.ValidateDefinition();
        if (validation.IsFailure)
        {
            var reason = validation.Error.ValidationErrors.FirstOrDefault()?.Message
                         ?? validation.Error.Description
                         ?? "Invalid identifier configuration.";
            throw new ArgumentException(reason, nameof(identifierConfig));
        }

        return identifierConfig.Normalize();
    }

    public void ScheduleActivation(DateTimeOffset effectiveFromUtc, DateTimeOffset? effectiveToUtc = null)
    {
        if (effectiveToUtc.HasValue && effectiveToUtc.Value <= effectiveFromUtc)
        {
            throw new ArgumentException("EffectiveToUtc must be later than EffectiveFromUtc.", nameof(effectiveToUtc));
        }

        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
        State = effectiveFromUtc <= DateTimeOffset.UtcNow
            ? SchemaLifecycleState.Active
            : SchemaLifecycleState.Scheduled;
    }

    public void Deprecate()
    {
        State = SchemaLifecycleState.Deprecated;
    }

    public void Retire()
    {
        State = SchemaLifecycleState.Retired;
    }

    #endregion
}
