using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EstateDataCollection.Infrastructure.Registry;

internal static partial class DraftSchemaBlobPathParser
{
    public const string RegexName = "name";
    public const string RegexVersion = "ver";

    private static readonly string[] IgnoredCompanionSuffixes =
    [
        ".error.json",
        ".valid.json",
        ".invalid.json",
        ".meta.json"
    ];

    private static readonly string[] KnownContractSuffixes =
    [
        ".odcs.yaml",
        ".odcs.yml",
        ".schema.json",
        ".rules.json",
        ".logic.json",
        ".yaml",
        ".yml",
        ".json"
    ];

    public static bool IsIgnoredCompanionBlob(string normalizedPath, string draftErrorBlobSuffix)
    {
        return IgnoredCompanionSuffixes.Any(s => normalizedPath.EndsWith(s, StringComparison.OrdinalIgnoreCase)) ||
               normalizedPath.EndsWith(draftErrorBlobSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRulesBlob(string blobPath, string content)
    {
        if (blobPath.EndsWith(".rules.json", StringComparison.OrdinalIgnoreCase) ||
            blobPath.EndsWith(".logic.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return content.TrimStart().StartsWith('[');
    }

    public static string NormaliseDraftBlobPath(string blobPath, string draftContainerName)
    {
        var normalized = blobPath.Trim().TrimStart('/');
        var containerPrefix = $"{draftContainerName}/";
        if (normalized.StartsWith(containerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[containerPrefix.Length..];
        }

        return normalized;
    }

    public static void TryParseDraftBlobPathConvention(
        string normalizedPath,
        out string? schemaName,
        out int? version,
        out string basePrefix)
    {
        schemaName = null;
        version = null;
        basePrefix = normalizedPath;

        foreach (var suffix in KnownContractSuffixes)
        {
            if (normalizedPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                basePrefix = normalizedPath[..^suffix.Length];
                break;
            }
        }

        // Pattern 1: {schemaName}/v{version}.{ext} or {schemaName}/{version}.{ext}
        var folderMatch = FolderVersionRegex().Match(basePrefix);
        if (folderMatch.Success)
        {
            schemaName = folderMatch.Groups[RegexName].Value;
            if (int.TryParse(folderMatch.Groups[RegexVersion].Value, out var v) && v > 0)
            {
                version = v;
            }
            return;
        }

        // Pattern 2: {schemaName}-v{version}.{ext} or {schemaName}_v{version}.{ext}
        var flatVersionMatch = FlatVersionRegex().Match(basePrefix);
        if (flatVersionMatch.Success)
        {
            schemaName = flatVersionMatch.Groups[RegexName].Value;
            if (int.TryParse(flatVersionMatch.Groups[RegexVersion].Value, out var v) && v > 0)
            {
                version = v;
            }
        }
    }

    public static string SerialiseFixturesArray(IReadOnlyList<string> fixtures)
    {
        var array = new JsonArray();
        foreach (var fixture in fixtures.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            try
            {
                var parsed = JsonNode.Parse(fixture);
                if (parsed is not null)
                {
                    array.Add(parsed);
                    continue;
                }
            }
            catch
            {
                // Preserve raw string if not valid JSON
            }

            array.Add(JsonValue.Create(fixture));
        }

        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    [GeneratedRegex($@"^(?<{RegexName}>[^/]+)/v?(?<{RegexVersion}>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FolderVersionRegex();

    [GeneratedRegex($@"^(?<{RegexName}>.+?)[-_]v(?<{RegexVersion}>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FlatVersionRegex();
}
