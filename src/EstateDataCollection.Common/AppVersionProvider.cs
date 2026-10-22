using System.Reflection;

namespace EstateDataCollection.Common;

/// <summary>
/// Provides application version metadata, resolving build-stamped CalVer InformationalVersion.
/// </summary>
public static class AppVersionProvider
{
    public static class Keys
    {
        public const string Version = "service.version";
        public const string InformativeVersion = "service.version.informative";
        public const string Sha = "service.sha";
    }
    
    private static readonly Lazy<VersionInfo> Info = new(ResolveVersionInfo);

    public static string Version => Info.Value.Version;
    public static string InformativeVersion => Info.Value.InformativeVersion;
    public static string Sha => Info.Value.Sha;

    private static VersionInfo ResolveVersionInfo()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersionProvider).Assembly;

        var assemblyVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (assemblyVersion == null)
        {
            var fallback = assembly.GetName().Version?.ToString() ?? "local";
            return new VersionInfo(fallback, fallback, "local");
        }

        var parts = assemblyVersion.InformationalVersion.Split('+');
        var calVer = parts[0];
        var sha = parts.Length > 1 ? parts[1] : "unknown";

        return new VersionInfo(assemblyVersion.InformationalVersion, calVer, sha);
    }

    private sealed record VersionInfo(string InformativeVersion, string Version, string Sha);
}
