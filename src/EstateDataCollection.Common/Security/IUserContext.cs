namespace EstateDataCollection.Common.Security;

/// <summary>
/// Represents the authenticated DfE operator or automation principal resolved from
/// Microsoft Entra ID credentials (App Service Easy Auth / JWT Bearer) on the Admin host.
/// </summary>
public interface IUserContext
{
    bool IsAuthenticated { get; }

    /// <summary>
    /// Microsoft Entra ID Object ID (`oid`) or subject (`sub` / `NameIdentifier`) when authenticated.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Microsoft Entra ID Tenant ID (`tid`) when available.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Operator display name (e.g. `name` or `userDetails` claim).
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// Operator email / UPN (`preferred_username` or `email` claim).
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Authentication type (e.g. "aad", "EasyAuthHeader", "Anonymous").
    /// </summary>
    string AuthenticationType { get; }

    /// <summary>
    /// App roles / role claims held by the authenticated principal.
    /// </summary>
    IReadOnlyCollection<string> Roles { get; }

    bool HasRole(string role);

    /// <summary>
    /// Concise formatted user string for Admin audit logs and telemetry, or null when anonymous.
    /// </summary>
    string? AuditUserDisplay { get; }
}
