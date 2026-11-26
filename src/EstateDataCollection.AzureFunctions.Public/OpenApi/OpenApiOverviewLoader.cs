using System.Reflection;

namespace EstateDataCollection.AzureFunctions.Public.OpenApi;

internal static class OpenApiOverviewLoader
{
    internal const string EmbeddedOverviewResourceName = "EstateDataCollection.AzureFunctions.Public.OpenApi.overview.md";

    internal const string DefaultFallbackDescription =
        "DfE Education Estates survey schema discovery and submission ingestion API, designed for exposure via DfE Find and Use an API (FaUAPI).";

    public static string LoadOverviewMarkdown()
    {
        var assembly = typeof(OpenApiOverviewLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedOverviewResourceName);
        if (stream is null)
        {
            return DefaultFallbackDescription;
        }

        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd().Trim();
        return string.IsNullOrWhiteSpace(content) ? DefaultFallbackDescription : content;
    }

    public static Uri? TryBuildWikiUri(string? wikiBaseUrl, string? slug = null)
    {
        if (string.IsNullOrWhiteSpace(wikiBaseUrl))
        {
            if (!string.IsNullOrWhiteSpace(slug) &&
                Uri.TryCreate(slug.Trim(), UriKind.Absolute, out var standaloneAbsolute) &&
                (standaloneAbsolute.Scheme == Uri.UriSchemeHttp || standaloneAbsolute.Scheme == Uri.UriSchemeHttps))
            {
                return standaloneAbsolute;
            }

            return null;
        }

        if (!string.IsNullOrWhiteSpace(slug) &&
            Uri.TryCreate(slug.Trim(), UriKind.Absolute, out var explicitUri) &&
            (explicitUri.Scheme == Uri.UriSchemeHttp || explicitUri.Scheme == Uri.UriSchemeHttps))
        {
            return explicitUri;
        }

        var trimmedBase = wikiBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmedBase, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return baseUri;
        }

        var combined = $"{trimmedBase}/{slug.Trim().TrimStart('/')}";
        return Uri.TryCreate(combined, UriKind.Absolute, out var resolved) ? resolved : baseUri;
    }
}
