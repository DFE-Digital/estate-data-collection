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
        string? urnPointer = null;
        if (http.Query.TryGetValue("urnPointer", out var qPtr) && !string.IsNullOrWhiteSpace(qPtr))
        {
            urnPointer = qPtr.ToString();
        }
        else if (http.Headers.TryGetValue("X-Urn-Pointer", out var hPtr) && !string.IsNullOrWhiteSpace(hPtr))
        {
            urnPointer = hPtr.ToString();
        }

        if (string.IsNullOrWhiteSpace(urnPointer))
        {
            return null;
        }

        bool? required = null;
        if (http.Query.TryGetValue("urnRequired", out var qReq) && bool.TryParse(qReq, out var parsedQReq))
        {
            required = parsedQReq;
        }
        else if (http.Headers.TryGetValue("X-Urn-Required", out var hReq) && bool.TryParse(hReq, out var parsedHReq))
        {
            required = parsedHReq;
        }

        return new SchemaIdentifierConfig
        {
            Type = "URN",
            JsonPointer = urnPointer,
            Required = required
        };
    }
}
