using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EstateDataCollection.Common.Security;
using Microsoft.AspNetCore.Http;

namespace EstateDataCollection.AzureFunctions.Admin.Security;

/// <summary>
/// Parses Azure App Service Authentication (Easy Auth) headers into a <see cref="ClaimsPrincipal"/>,
/// populates <see cref="IUserContext"/> / <see cref="UserContext"/>, and extracts operator display names
/// without requiring ASP.NET Core cookie middleware.
/// </summary>
public static class EasyAuthPrincipalParser
{
    public const string ClientPrincipalHeader = "X-MS-CLIENT-PRINCIPAL";
    public const string ClientPrincipalNameHeader = "X-MS-CLIENT-PRINCIPAL-NAME";

    private static readonly string[] UserIdClaimTypes =
    [
        "oid",
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        ClaimTypes.NameIdentifier,
        "sub"
    ];

    private static readonly string[] TenantIdClaimTypes =
    [
        "tid",
        "http://schemas.microsoft.com/identity/claims/tenantid"
    ];

    private static readonly string[] EmailClaimTypes =
    [
        "preferred_username",
        "upn",
        ClaimTypes.Upn,
        ClaimTypes.Email,
        "email"
    ];

    private static readonly string[] DisplayNameClaimTypes =
    [
        "name",
        "preferred_username",
        ClaimTypes.Name,
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
        ClaimTypes.Email,
        "email"
    ];

    private static readonly string[] RoleClaimTypes =
    [
        ClaimTypes.Role,
        "roles",
        "role",
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    ];

    public static ClaimsPrincipal? ParseFromHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (headers.TryGetValue(ClientPrincipalHeader, out var encodedPrincipal) &&
            !string.IsNullOrWhiteSpace(encodedPrincipal))
        {
            var principal = TryParseClientPrincipal(encodedPrincipal.ToString());
            if (principal is not null)
            {
                return principal;
            }
        }

        if (headers.TryGetValue(ClientPrincipalNameHeader, out var principalName) &&
            !string.IsNullOrWhiteSpace(principalName))
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, principalName.ToString().Trim())],
                authenticationType: "EasyAuthHeader");
            return new ClaimsPrincipal(identity);
        }

        return null;
    }

    public static void PopulateUserContext(ClaimsPrincipal? principal, UserContext userContext)
    {
        ArgumentNullException.ThrowIfNull(userContext);

        if (principal?.Identity?.IsAuthenticated != true)
        {
            userContext.Reset();
            return;
        }

        var userId = FindFirstClaim(principal, UserIdClaimTypes);
        var tenantId = FindFirstClaim(principal, TenantIdClaimTypes);
        var email = FindFirstClaim(principal, EmailClaimTypes);
        var displayName = FindFirstClaim(principal, DisplayNameClaimTypes)
                          ?? (string.IsNullOrWhiteSpace(principal.Identity.Name) ? null : principal.Identity.Name);
        var roles = principal.Claims
            .Where(c => RoleClaimTypes.Any(rt => string.Equals(c.Type, rt, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        userContext.SetEntraUser(
            userId: userId,
            tenantId: tenantId,
            displayName: displayName,
            email: email,
            authenticationType: principal.Identity.AuthenticationType ?? "aad",
            roles: roles);
    }

    public static bool HasRole(ClaimsPrincipal? principal, string requiredRole)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requiredRole))
        {
            return true;
        }

        if (principal.IsInRole(requiredRole))
        {
            return true;
        }

        return principal.Claims.Any(c =>
            RoleClaimTypes.Any(rt => string.Equals(c.Type, rt, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(c.Value, requiredRole, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasAnyRole(ClaimsPrincipal? principal, IEnumerable<string> acceptedRoles)
    {
        ArgumentNullException.ThrowIfNull(acceptedRoles);

        foreach (var role in acceptedRoles)
        {
            if (HasRole(principal, role))
            {
                return true;
            }
        }

        return false;
    }

    public static string? ResolveDisplayName(HttpRequest http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (http.Headers.TryGetValue(ClientPrincipalNameHeader, out var principalName) &&
            !string.IsNullOrWhiteSpace(principalName))
        {
            return principalName.ToString().Trim();
        }

        if (http.Headers.TryGetValue(ClientPrincipalHeader, out var encodedPrincipal) &&
            !string.IsNullOrWhiteSpace(encodedPrincipal))
        {
            var decoded = TryExtractDisplayName(encodedPrincipal.ToString());
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return decoded;
            }
        }

        var identityName = http.HttpContext?.User?.Identity?.Name;
        return string.IsNullOrWhiteSpace(identityName) ? null : identityName;
    }

    private static string? FindFirstClaim(ClaimsPrincipal principal, string[] claimTypes)
    {
        foreach (var targetType in claimTypes)
        {
            var match = principal.Claims.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.Value) &&
                string.Equals(c.Type, targetType, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(match?.Value))
            {
                return match.Value.Trim();
            }
        }

        return null;
    }

    private static ClaimsPrincipal? TryParseClientPrincipal(string encoded)
    {
        try
        {
            var bytes = Convert.FromBase64String(encoded.Trim());
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            var root = doc.RootElement;

            var authType = GetOptionalString(root, "auth_typ") ?? "aad";
            var nameType = GetOptionalString(root, "name_typ") ?? ClaimTypes.Name;
            var roleType = GetOptionalString(root, "role_typ") ?? ClaimTypes.Role;

            var claims = new List<Claim>();

            if (root.TryGetProperty("claims", out var claimsArray) && claimsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in claimsArray.EnumerateArray())
                {
                    var typ = GetOptionalString(item, "typ");
                    var val = GetOptionalString(item, "val");
                    if (!string.IsNullOrWhiteSpace(typ) && !string.IsNullOrWhiteSpace(val))
                    {
                        claims.Add(new Claim(typ, val));
                    }
                }
            }

            var userDetails = GetOptionalString(root, "userDetails");
            if (!string.IsNullOrWhiteSpace(userDetails) &&
                !claims.Any(c => string.Equals(c.Type, nameType, StringComparison.OrdinalIgnoreCase)))
            {
                claims.Add(new Claim(nameType, userDetails));
            }

            if (claims.Count == 0)
            {
                return null;
            }

            var identity = new ClaimsIdentity(claims, authType, nameType, roleType);
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractDisplayName(string encoded)
    {
        try
        {
            var bytes = Convert.FromBase64String(encoded.Trim());
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            var root = doc.RootElement;

            var userDetails = GetOptionalString(root, "userDetails");
            if (!string.IsNullOrWhiteSpace(userDetails))
            {
                return userDetails;
            }

            if (root.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Array)
            {
                var claimPairs = new List<(string? Typ, string? Val)>();
                foreach (var claim in claims.EnumerateArray())
                {
                    claimPairs.Add((GetOptionalString(claim, "typ"), GetOptionalString(claim, "val")));
                }

                foreach (var targetType in DisplayNameClaimTypes)
                {
                    var match = claimPairs.FirstOrDefault(c =>
                        !string.IsNullOrWhiteSpace(c.Val) &&
                        string.Equals(c.Typ, targetType, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrWhiteSpace(match.Val))
                    {
                        return match.Val;
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed client principal header
        }

        return null;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) &&
               prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
