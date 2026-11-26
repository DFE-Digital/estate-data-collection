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
    public SchemaValidationPolicy? ValidationPolicy { get; private set; }

    [JsonIgnore]
    public SchemaValidationPolicy EffectiveValidationPolicy => ValidationPolicy ?? SchemaValidationPolicy.Default;

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

    [JsonInclude]
    public string? SignedOffBy { get; private set; }

    [JsonInclude]
    public DateTimeOffset? SignedOffAtUtc { get; private set; }

    [JsonInclude]
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    [JsonInclude]
    public bool HasErrorReport { get; private set; }

    [JsonInclude]
    public string? ErrorSummary { get; private set; }

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
        SchemaIdentifierConfig? identifierConfig = null,
        SchemaValidationPolicy? validationPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJsonSchema);

        var normalizedConfig = ValidateAndNormaliseIdentifier(identifierConfig);
        var normalizedPolicy = ValidateAndNormalisePolicy(validationPolicy);

        return new SchemaMetadata
        {
            Name = name,
            Version = version,
            RawJsonSchema = rawJsonSchema,
            Sha256Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJsonSchema))),
            State = SchemaLifecycleState.Draft,
            IdentifierConfig = normalizedConfig,
            ValidationPolicy = normalizedPolicy
        };
    }

    /// <summary>
    /// Factory for consuming a schema contract that failed hard pre-flight checks into <see cref="SchemaLifecycleState.Invalid"/>.
    /// Reserves the version key in the registry alongside <c>.meta.json</c> and <c>.error.json</c>.
    /// </summary>
    public static SchemaMetadata CreateInvalid(
        string name,
        int version,
        string? rawJsonSchema,
        SchemaIdentifierConfig? identifierConfig = null,
        SchemaValidationPolicy? validationPolicy = null,
        string? errorSummary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        var schemaText = rawJsonSchema ?? string.Empty;
        SchemaIdentifierConfig? safeIdConfig = null;
        try
        {
            safeIdConfig = ValidateAndNormaliseIdentifier(identifierConfig);
        }
        catch
        {
            safeIdConfig = identifierConfig;
        }

        SchemaValidationPolicy? safePolicy = null;
        try
        {
            safePolicy = ValidateAndNormalisePolicy(validationPolicy);
        }
        catch
        {
            safePolicy = validationPolicy;
        }

        return new SchemaMetadata
        {
            Name = name,
            Version = version,
            RawJsonSchema = schemaText,
            Sha256Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schemaText))),
            State = SchemaLifecycleState.Invalid,
            IdentifierConfig = safeIdConfig,
            ValidationPolicy = safePolicy,
            HasErrorReport = true,
            ErrorSummary = string.IsNullOrWhiteSpace(errorSummary) ? null : errorSummary.Trim()
        };
    }

    #endregion

    #region Capacilities Explicit Invariants

    /// <summary>
    /// Can this schema accept any traffic? (Active versions and Deprecated versions in grace period)
    /// </summary>
    public bool CanAcceptTraffic => State is SchemaLifecycleState.Active or SchemaLifecycleState.Deprecated;

    /// <summary>
    /// Is this eligible to be the default version when no explicit version is requested?
    /// </summary>
    public bool CanBeDefault => State is SchemaLifecycleState.Active;

    /// <summary>
    /// Can the schema definition still be edited or overwritten?
    /// </summary>
    public bool IsMutable => State is SchemaLifecycleState.Draft;

    /// <summary>
    /// Can dry-run test payloads be executed against this schema?
    /// </summary>
    public bool AllowsDryRun => State is SchemaLifecycleState.Draft or SchemaLifecycleState.Validated or SchemaLifecycleState.Unvalidated;

    /// <summary>
    /// Can the schema be used
    /// </summary>
    public bool IsRetired => State is SchemaLifecycleState.Retired;

    public bool IsDeleted => State is SchemaLifecycleState.Deleted;

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

    public bool IsUpcomingAt(DateTimeOffset timestamp)
    {
        return State == SchemaLifecycleState.Scheduled &&
               EffectiveFromUtc is not null &&
               timestamp < EffectiveFromUtc.Value;
    }

    public bool IsRetiredAt(DateTimeOffset timestamp)
    {
        return State == SchemaLifecycleState.Retired ||
               (State is SchemaLifecycleState.Active or SchemaLifecycleState.Scheduled &&
                EffectiveToUtc is not null &&
                timestamp >= EffectiveToUtc.Value);
    }

    public bool IsVisibleForDiscovery(
        DateTimeOffset timestamp,
        bool includeUnscheduled = false,
        bool includeUpcoming = false,
        bool includeRetired = false)
    {
        if (CanAcceptTrafficAt(timestamp))
        {
            return true;
        }

        if (includeUpcoming && IsUpcomingAt(timestamp))
        {
            return true;
        }

        if (includeRetired && IsRetiredAt(timestamp))
        {
            return true;
        }

        return includeUnscheduled &&
               State is SchemaLifecycleState.Unassigned
                   or SchemaLifecycleState.Draft
                   or SchemaLifecycleState.Validated
                   or SchemaLifecycleState.Invalid
                   or SchemaLifecycleState.Unvalidated
                   or SchemaLifecycleState.Deleted;
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
        HasErrorReport = false;
        ErrorSummary = null;
        State = SchemaLifecycleState.Validated;
    }

    /// <summary>
    /// Marks the schema as <see cref="SchemaLifecycleState.Unvalidated"/> when hard checks passed
    /// but warnings were recorded and require explicit sign-off before scheduling.
    /// </summary>
    public void MarkUnvalidated(string? errorSummary = null)
    {
        EffectiveFromUtc = null;
        EffectiveToUtc = null;
        HasErrorReport = true;
        ErrorSummary = string.IsNullOrWhiteSpace(errorSummary) ? null : errorSummary.Trim();
        State = SchemaLifecycleState.Unvalidated;
    }

    /// <summary>
    /// Marks the schema as <see cref="SchemaLifecycleState.Invalid"/> when hard checks failed.
    /// </summary>
    public void MarkInvalid(string? errorSummary = null)
    {
        EffectiveFromUtc = null;
        EffectiveToUtc = null;
        HasErrorReport = true;
        ErrorSummary = string.IsNullOrWhiteSpace(errorSummary) ? null : errorSummary.Trim();
        State = SchemaLifecycleState.Invalid;
    }

    /// <summary>
    /// Explicitly signs off an <see cref="SchemaLifecycleState.Unvalidated"/> schema version,
    /// transitioning it to <see cref="SchemaLifecycleState.Validated"/>.
    /// </summary>
    public void SignOff(string signedOffBy)
    {
        if (State != SchemaLifecycleState.Unvalidated)
        {
            throw new InvalidOperationException($"Only Unvalidated schemas can be signed off. Current state: '{State}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(signedOffBy);

        SignedOffBy = signedOffBy.Trim();
        SignedOffAtUtc = DateTimeOffset.UtcNow;
        HasErrorReport = false;
        ErrorSummary = null;
        State = SchemaLifecycleState.Validated;
    }

    /// <summary>
    /// Marks the schema version as a <see cref="SchemaLifecycleState.Deleted"/> tombstone
    /// while preserving <c>.meta.json</c> and <c>.error.json</c> so the version key remains consumed.
    /// </summary>
    public void MarkDeleted()
    {
        if (State == SchemaLifecycleState.Deleted)
        {
            return;
        }

        EffectiveFromUtc = null;
        EffectiveToUtc = null;
        DeletedAtUtc = DateTimeOffset.UtcNow;
        State = SchemaLifecycleState.Deleted;
    }

    /// <summary>
    /// Configures or updates the per-schema-version establishment identifier (e.g. URN, UKPRN, LAEstab, custom pointer) metadata.
    /// </summary>
    public void ConfigureIdentifier(SchemaIdentifierConfig? identifierConfig)
    {
        IdentifierConfig = ValidateAndNormaliseIdentifier(identifierConfig);
    }

    /// <summary>
    /// Configures or updates the per-schema-version validation policy (severity gating, workflow pipeline override, draft reference policy).
    /// </summary>
    public void ConfigureValidationPolicy(SchemaValidationPolicy? validationPolicy)
    {
        ValidationPolicy = ValidateAndNormalisePolicy(validationPolicy);
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

    public void UpdateErrorReportStatus(bool hasErrorReport, string? errorSummary = null)
    {
        HasErrorReport = hasErrorReport;
        ErrorSummary = string.IsNullOrWhiteSpace(errorSummary) ? null : errorSummary.Trim();
    }

    private static SchemaIdentifierConfig? ValidateAndNormaliseIdentifier(SchemaIdentifierConfig? identifierConfig)
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

        return identifierConfig.Normalise();
    }

    private static SchemaValidationPolicy? ValidateAndNormalisePolicy(SchemaValidationPolicy? validationPolicy)
    {
        if (validationPolicy is null)
        {
            return null;
        }

        var validation = validationPolicy.ValidateDefinition();
        if (validation.IsFailure)
        {
            var reason = validation.Error.ValidationErrors.FirstOrDefault()?.Message
                         ?? validation.Error.Description
                         ?? "Invalid validation policy configuration.";
            throw new ArgumentException(reason, nameof(validationPolicy));
        }

        return validationPolicy.Normalise();
    }

    public void ScheduleActivation(DateTimeOffset effectiveFromUtc, DateTimeOffset? effectiveToUtc = null)
    {
        if (State is SchemaLifecycleState.Invalid or SchemaLifecycleState.Unvalidated or SchemaLifecycleState.Deleted)
        {
            throw new InvalidOperationException($"Schema in state '{State}' cannot be scheduled or activated.");
        }

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
        if (State is SchemaLifecycleState.Invalid or SchemaLifecycleState.Unvalidated or SchemaLifecycleState.Deleted)
        {
            throw new InvalidOperationException($"Schema in state '{State}' cannot be deprecated.");
        }

        State = SchemaLifecycleState.Deprecated;
    }

    public void Retire()
    {
        if (State is SchemaLifecycleState.Invalid or SchemaLifecycleState.Deleted)
        {
            throw new InvalidOperationException($"Schema in state '{State}' cannot be retired.");
        }

        State = SchemaLifecycleState.Retired;
    }

    #endregion
}
