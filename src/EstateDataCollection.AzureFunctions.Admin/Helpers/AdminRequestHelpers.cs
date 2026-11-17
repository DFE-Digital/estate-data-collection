using System.Text.Json;
using EstateDataCollection.Domain.Schema;
using Microsoft.AspNetCore.Http;

namespace EstateDataCollection.AzureFunctions.Admin.Helpers;

internal static class AdminRequestHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool IsYamlRequest(HttpRequest http, string rawBody)
    {
        if (!string.IsNullOrWhiteSpace(http.ContentType) &&
            http.ContentType.Contains("yaml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = GetFileNameHint(http);
        if (!string.IsNullOrWhiteSpace(fileName) &&
            (fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
             fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
        {
            var trimmed = rawBody.AsSpan().TrimStart();
            if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
            {
                return true;
            }
        }

        return false;
    }

    public static string? GetFileNameHint(HttpRequest http)
    {
        if (http.Query.TryGetValue("fileName", out var qFile) && !string.IsNullOrWhiteSpace(qFile))
        {
            return qFile.ToString();
        }

        if (http.Headers.TryGetValue("X-File-Name", out var hFile) && !string.IsNullOrWhiteSpace(hFile))
        {
            return hFile.ToString();
        }

        return null;
    }

    public static SchemaIdentifierConfig? GetIdentifierConfigHint(HttpRequest http)
    {
        var identifierType = TryGetQueryOrHeader(http, ["identifierType", "urnType"], ["X-Identifier-Type", "X-Urn-Type"]);
        var identifierPointer = TryGetQueryOrHeader(http, ["identifierPointer", "urnPointer"], ["X-Identifier-Pointer", "X-Urn-Pointer"]);

        if (string.IsNullOrWhiteSpace(identifierType) && string.IsNullOrWhiteSpace(identifierPointer))
        {
            return null;
        }

        bool? required = null;
        var rawRequired = TryGetQueryOrHeader(http, ["identifierRequired", "urnRequired"], ["X-Identifier-Required", "X-Urn-Required"]);
        if (!string.IsNullOrWhiteSpace(rawRequired) && bool.TryParse(rawRequired, out var parsedReq))
        {
            required = parsedReq;
        }

        return new SchemaIdentifierConfig
        {
            Type = string.IsNullOrWhiteSpace(identifierType)
                ? SchemaIdentifierConfig.DefaultUrnType
                : identifierType.Trim(),
            JsonPointer = string.IsNullOrWhiteSpace(identifierPointer) ? null : identifierPointer.Trim(),
            Required = required
        };
    }

    private static string? TryGetQueryOrHeader(HttpRequest http, string[] queryKeys, string[] headerKeys)
    {
        foreach (var qk in queryKeys)
        {
            if (http.Query.TryGetValue(qk, out var qv) && !string.IsNullOrWhiteSpace(qv))
            {
                return qv.ToString();
            }
        }

        foreach (var hk in headerKeys)
        {
            if (http.Headers.TryGetValue(hk, out var hv) && !string.IsNullOrWhiteSpace(hv))
            {
                return hv.ToString();
            }
        }

        return null;
    }
}
