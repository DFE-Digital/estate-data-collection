using System.Security.Cryptography;
using System.Text;

namespace EstateDataCollection.Domain.Schema;

public class SchemaMetadata
{
    public required string Name { get; init; }
    public required int Version { get; init; }
    public required string RawJsonSchema { get; init; }
    public required string Sha256Hash { get; init; }

    public SchemaLifecycleState State { get; private set; }

    public DateTimeOffset? EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    
    #region Domain Factories

    /// <summary>
    /// Factory for creating a brand new schema in Draft state.
    /// Computes hash and enforces core domain invariants.
    /// </summary>
    public static SchemaMetadata CreateDraft(string name, int version, string rawJsonSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJsonSchema);

        return new SchemaMetadata
        {
            Name = name,
            Version = version,
            RawJsonSchema = rawJsonSchema,
            Sha256Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJsonSchema))),
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

    public void ScheduleActivation(DateTimeOffset effectiveFromUtc, DateTimeOffset? effectiveToUtc = null)
    {
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