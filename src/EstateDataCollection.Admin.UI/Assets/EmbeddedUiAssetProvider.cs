using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using EstateDataCollection.Common;

namespace EstateDataCollection.Admin.UI.Assets;

public static class EmbeddedUiAssetProvider
{
    private sealed record CachedAsset(byte[] Bytes, string ContentType, string ETag);

    private static readonly Assembly ResourceAssembly = typeof(EmbeddedUiAssetProvider).Assembly;
    private static readonly ConcurrentDictionary<string, CachedAsset?> AssetCache = new(StringComparer.OrdinalIgnoreCase);

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

        var cached = AssetCache.GetOrAdd(sanitized, LoadAsset);
        if (cached is null)
        {
            return false;
        }

        bytes = cached.Bytes;
        contentType = cached.ContentType;
        etag = cached.ETag;
        return true;
    }

    private static CachedAsset? LoadAsset(string sanitized)
    {
        var stream = ResourceAssembly.GetManifestResourceStream($"wwwroot/{sanitized}")
            ?? ResourceAssembly.GetManifestResourceStream($"wwwroot/assets/{sanitized}");

        if (stream is null)
        {
            return null;
        }

        byte[] loadedBytes;
        using (stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            loadedBytes = ms.ToArray();
        }

        var ext = Path.GetExtension(sanitized);
        var mappedContentType = ContentTypes.TryGetValue(ext, out var mapped)
            ? mapped
            : "application/octet-stream";

        var hashHex = Convert.ToHexStringLower(SHA256.HashData(loadedBytes))[..16];
        var etag = $"\"{AppVersionProvider.Version}-{hashHex}\"";
        return new CachedAsset(loadedBytes, mappedContentType, etag);
    }
}
