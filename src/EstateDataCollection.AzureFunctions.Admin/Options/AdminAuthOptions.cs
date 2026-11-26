using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Security;

namespace EstateDataCollection.AzureFunctions.Admin.Options;

public sealed class AdminAuthOptions : IOption<AdminAuthOptions>
{
    public static string SectionName
    {
        get { return "Security:AdminAuth"; }
    }

    /// <summary>
    /// Enables attribute-driven Microsoft Entra ID / Easy Auth enforcement inside the Admin worker.
    /// Defaults to false so local development (`func start`) and Azurite integration tests run without Easy Auth.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Maps logical role keys declared in <see cref="RequireRolesAttribute"/> (e.g. <c>Operator</c>,
    /// <c>Automation</c>) to one or more Entra ID App Role names or Group IDs (comma/semicolon-separated).
    /// Setting a mapping value to an empty string allows any authenticated Entra ID user for that key.
    /// Any unmapped role name in <see cref="RequireRolesAttribute"/> is matched verbatim against Entra claims.
    /// </summary>
    public Dictionary<string, string> RoleMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [AdminRoleKeys.Operator] = "Edc.Admin.Operator",
        [AdminRoleKeys.Automation] = "Edc.Admin.Automation"
    };

    /// <summary>
    /// App Service Easy Auth login callback entry point used for browser redirects on interactive GET/HEAD requests.
    /// </summary>
    public string LoginPath { get; init; } = "/.auth/login/aad";

    public (bool AllowAnyAuthenticated, IReadOnlyList<string> AcceptedRoles) ResolveAcceptedRoles(string roleOrKey)
    {
        if (string.IsNullOrWhiteSpace(roleOrKey))
        {
            return (true, []);
        }

        var trimmed = roleOrKey.Trim();
        if (RoleMappings.TryGetValue(trimmed, out var mappedValue))
        {
            if (string.IsNullOrWhiteSpace(mappedValue))
            {
                return (true, []);
            }

            var split = mappedValue
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return split.Length == 0
                ? (true, [])
                : (false, split);
        }

        return (false, [trimmed]);
    }
}
