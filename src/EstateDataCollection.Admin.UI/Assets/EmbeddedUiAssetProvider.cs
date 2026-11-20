using System.Reflection;
using System.Security.Cryptography;
using EstateDataCollection.Common;

namespace EstateDataCollection.Admin.UI.Assets;

public static class EmbeddedUiAssetProvider
{
    private static readonly Assembly ResourceAssembly = typeof(EmbeddedUiAssetProvider).Assembly;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2"
    };

    public static bool TryReadAsset(string? relativePath, out byte[] bytes, out string contentType)
        => TryReadAsset(relativePath, out bytes, out contentType, out _);

    public static bool TryReadAsset(
        string? relativePath,
        out byte[] bytes,
        out string contentType,
        out string etag)
    {
        bytes = [];
        contentType = "application/octet-stream";
        etag = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var sanitized = relativePath.Trim().TrimStart('/').Replace('\\', '/');
        if (sanitized.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var stream = ResourceAssembly.GetManifestResourceStream($"wwwroot/{sanitized}")
            ?? ResourceAssembly.GetManifestResourceStream($"wwwroot/assets/{sanitized}");

        if (stream is null)
        {
            return false;
        }

        using (stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        var ext = Path.GetExtension(sanitized);
        if (ContentTypes.TryGetValue(ext, out var mapped))
        {
            contentType = mapped;
        }

        var hashHex = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
        var version = AppVersionProvider.Version;
        etag = $"\"{version}-{hashHex}\"";

        return true;
    }
}
