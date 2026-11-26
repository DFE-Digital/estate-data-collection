using System.Collections.Frozen;
using System.Reflection;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.Common.Web.Security;

public sealed record FunctionHostAssemblyMarker(Assembly HostAssembly);

public interface IFunctionAuthMetadataResolver
{
    EndpointAuthMetadata Resolve(FunctionContext context);
    EndpointAuthMetadata ResolveByFunctionName(string functionName);
}

/// <summary>
/// Scans registered Function host assemblies once at startup for <c>[AllowAnonymous]</c> and
/// <see cref="RequireRolesAttribute"/> declarations and caches them for zero-reflection per-request lookup.
/// </summary>
public sealed class FunctionAuthMetadataResolver : IFunctionAuthMetadataResolver
{
    private readonly FrozenDictionary<string, EndpointAuthMetadata> _byFunctionName;
    private readonly FrozenDictionary<string, EndpointAuthMetadata> _byEntryPoint;

    public FunctionAuthMetadataResolver(Assembly hostAssembly)
        : this([new FunctionHostAssemblyMarker(hostAssembly)])
    {
    }

    public FunctionAuthMetadataResolver(IEnumerable<FunctionHostAssemblyMarker> assemblyMarkers)
    {
        ArgumentNullException.ThrowIfNull(assemblyMarkers);

        var byName = new Dictionary<string, EndpointAuthMetadata>(StringComparer.OrdinalIgnoreCase);
        var byEntry = new Dictionary<string, EndpointAuthMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblyMarkers.Select(m => m.HostAssembly).Distinct())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type is not { IsClass: true, IsAbstract: false } ||
                    !typeof(IFunctionEndpoint).IsAssignableFrom(type))
                {
                    continue;
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var functionAttr = method.GetCustomAttribute<FunctionAttribute>();
                    if (functionAttr is null)
                    {
                        continue;
                    }

                    var metadata = ExtractMetadata(functionAttr.Name, type, method);
                    byName[metadata.FunctionName] = metadata;
                    if (!string.IsNullOrWhiteSpace(metadata.EntryPoint))
                    {
                        byEntry[metadata.EntryPoint] = metadata;
                    }
                }
            }
        }

        _byFunctionName = byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byEntryPoint = byEntry.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public EndpointAuthMetadata Resolve(FunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_byFunctionName.TryGetValue(context.FunctionDefinition.Name, out var byName))
        {
            return byName;
        }

        if (!string.IsNullOrWhiteSpace(context.FunctionDefinition.EntryPoint) &&
            _byEntryPoint.TryGetValue(context.FunctionDefinition.EntryPoint, out var byEntry))
        {
            return byEntry;
        }

        return EndpointAuthMetadata.DefaultProtected;
    }

    public EndpointAuthMetadata ResolveByFunctionName(string functionName)
    {
        if (!string.IsNullOrWhiteSpace(functionName) &&
            _byFunctionName.TryGetValue(functionName, out var metadata))
        {
            return metadata;
        }

        return EndpointAuthMetadata.DefaultProtected;
    }

    public static bool IsAllowAnonymous(Type? endpointType, MethodInfo? method)
    {
        return (endpointType is not null && endpointType.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)) ||
               (method is not null && method.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
    }

    public static EndpointAuthMetadata ExtractMetadata(string functionName, Type endpointType, MethodInfo method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        ArgumentNullException.ThrowIfNull(endpointType);
        ArgumentNullException.ThrowIfNull(method);

        var entryPoint = $"{endpointType.FullName}.{method.Name}";

        if (IsAllowAnonymous(endpointType, method))
        {
            return new EndpointAuthMetadata(
                functionName,
                entryPoint,
                AllowAnonymous: true,
                RoleRequirements: []);
        }

        var roleRequirements = new List<IReadOnlyList<string>>();

        foreach (var attr in endpointType.GetCustomAttributes<RequireRolesAttribute>(inherit: true))
        {
            if (attr.Roles.Count > 0)
            {
                roleRequirements.Add(attr.Roles);
            }
        }

        foreach (var attr in method.GetCustomAttributes<RequireRolesAttribute>(inherit: false))
        {
            if (attr.Roles.Count > 0)
            {
                roleRequirements.Add(attr.Roles);
            }
        }

        return new EndpointAuthMetadata(
            functionName,
            entryPoint,
            AllowAnonymous: false,
            RoleRequirements: roleRequirements);
    }
}
