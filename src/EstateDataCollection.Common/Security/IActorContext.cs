namespace EstateDataCollection.Common.Security;

/// <summary>
/// Represents the authenticated machine/organisation actor (e.g. a surveyor system calling via
/// DfE Find and Use an API / APIM subscription key or OAuth 2.0 client_credentials token)
/// for the current request scope.
/// </summary>
public interface IActorContext
{
    bool IsAuthenticated { get; }

    /// <summary>
    /// Stable non-secret actor identifier (e.g. FaUAPI subscription ID, OAuth client/app ID,
    /// or non-reversible SHA-256 fingerprint prefix of the subscription key).
    /// </summary>
    string? ActorId { get; }

    /// <summary>
    /// Friendly display name for the surveyor / consumer organisation when known.
    /// </summary>
    string? ActorName { get; }

    /// <summary>
    /// FaUAPI / APIM subscription identifier if supplied or mapped.
    /// </summary>
    string? SubscriptionId { get; }

    /// <summary>
    /// FaUAPI / APIM subscription display name if forwarded by the gateway.
    /// </summary>
    string? SubscriptionName { get; }

    /// <summary>
    /// Authentication scheme used (e.g. "FaUApiSubscriptionKey", "FaUApiOAuth2", "Anonymous").
    /// </summary>
    string AuthenticationType { get; }

    /// <summary>
    /// Non-secret metadata attributes associated with the actor (safe for telemetry and audit logs).
    /// </summary>
    IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Concise formatted actor string persisted to submission audit entries, or null when anonymous.
    /// </summary>
    string? AuditActorDisplay { get; }
}
