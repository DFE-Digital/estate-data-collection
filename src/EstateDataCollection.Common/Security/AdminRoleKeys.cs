namespace EstateDataCollection.Common.Security;

/// <summary>
/// Logical role keys resolved via <c>Security:AdminAuth:RoleMappings</c> (or matched verbatim if unmapped).
/// Using keys avoids hardcoding environment-specific Entra ID App Role names or Group IDs into endpoint classes.
/// </summary>
public static class AdminRoleKeys
{
    public const string Operator = "Operator";
    public const string Automation = "Automation";
}
