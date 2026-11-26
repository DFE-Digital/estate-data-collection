namespace EstateDataCollection.Common.Web.Security;

/// <summary>
/// Cached authorization metadata extracted at startup from a function endpoint class and its
/// <c>[Function]</c> entry-point method.
/// </summary>
public sealed record EndpointAuthMetadata(
    string FunctionName,
    string EntryPoint,
    bool AllowAnonymous,
    IReadOnlyList<IReadOnlyList<string>> RoleRequirements)
{
    public static EndpointAuthMetadata DefaultProtected { get; } = new(
        FunctionName: string.Empty,
        EntryPoint: string.Empty,
        AllowAnonymous: false,
        RoleRequirements: []);

    public static EndpointAuthMetadata Anonymous { get; } = new(
        FunctionName: string.Empty,
        EntryPoint: string.Empty,
        AllowAnonymous: true,
        RoleRequirements: []);

    public static EndpointAuthMetadata RequireRole(params string[] roles)
    {
        var normalized = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0
            ? DefaultProtected
            : new EndpointAuthMetadata(
                FunctionName: string.Empty,
                EntryPoint: string.Empty,
                AllowAnonymous: false,
                RoleRequirements: [normalized]);
    }
}
