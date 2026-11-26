namespace EstateDataCollection.Common.Security;

/// <summary>
/// Default scoped implementation of <see cref="IUserContext"/> populated per HTTP invocation
/// by Admin authentication middleware from Microsoft Entra ID credentials.
/// </summary>
public sealed class UserContext : IUserContext
{
    private List<string> _roles = [];

    public bool IsAuthenticated { get; private set; }
    public string? UserId { get; private set; }
    public string? TenantId { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Email { get; private set; }
    public string AuthenticationType { get; private set; } = "Anonymous";

    public IReadOnlyCollection<string> Roles
    {
        get { return _roles; }
    }

    public string? AuditUserDisplay
    {
        get
        {
            if (!IsAuthenticated)
            {
                return null;
            }

            var label = !string.IsNullOrWhiteSpace(Email)
                ? Email
                : !string.IsNullOrWhiteSpace(DisplayName)
                    ? DisplayName
                    : UserId;

            if (string.IsNullOrWhiteSpace(label))
            {
                return AuthenticationType;
            }

            return !string.IsNullOrWhiteSpace(UserId) &&
                   !string.Equals(label, UserId, StringComparison.Ordinal)
                ? $"{label} ({UserId})"
                : label;
        }
    }

    public bool HasRole(string role)
    {
        if (!IsAuthenticated)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            return true;
        }

        return _roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    }

    public void SetEntraUser(
        string? userId,
        string? tenantId,
        string? displayName,
        string? email,
        string authenticationType,
        IEnumerable<string>? roles = null)
    {
        IsAuthenticated = true;
        UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        AuthenticationType = string.IsNullOrWhiteSpace(authenticationType) ? "aad" : authenticationType.Trim();
        _roles = roles is null
            ? []
            : roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    public void Reset()
    {
        IsAuthenticated = false;
        UserId = null;
        TenantId = null;
        DisplayName = null;
        Email = null;
        AuthenticationType = "Anonymous";
        _roles = [];
    }
}
