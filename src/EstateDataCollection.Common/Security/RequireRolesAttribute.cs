namespace EstateDataCollection.Common.Security;

/// <summary>
/// Declares one or more roles (or logical role keys resolved via configuration) required to invoke
/// a function endpoint or page. Passing multiple roles in one attribute requires any one of them (OR);
/// applying multiple attributes requires all of them (AND).
/// Overridden by <c>[AllowAnonymous]</c> on a more specific class or <c>[Function]</c> method.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireRolesAttribute(params string[] roles) : Attribute
{
    public IReadOnlyList<string> Roles { get; } = roles
        .Where(r => !string.IsNullOrWhiteSpace(r))
        .Select(r => r.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
