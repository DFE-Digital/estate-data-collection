using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EstateDataCollection.AzureFunctions.Public.Options;
using EstateDataCollection.Common.Security;
using Microsoft.AspNetCore.Http;

namespace EstateDataCollection.AzureFunctions.Public.Security;

/// <summary>
/// Resolves <see cref="IActorContext"/> from DfE Find and Use an API (FaUAPI) subscription key
/// headers, forwarded APIM subscription metadata headers, and/or future OAuth 2.0 Bearer claims.
/// Raw subscription keys are hashed into non-reversible fingerprints before entering audit/telemetry state.
/// </summary>
public static class FaUApiActorResolver
{
    private static readonly string[] OAuthClientIdClaimTypes =
    [
        "azp",
        "appid",
        "client_id",
        "oid",
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        "sub",
        ClaimTypes.NameIdentifier
    ];

    private static readonly string[] OAuthNameClaimTypes =
    [
        "name",
        "client_name",
        "preferred_username",
        ClaimTypes.Name
    ];

    public static ActorContext Resolve(
        HttpContext httpContext,
        PublicAuthOptions options,
        ActorContext? target = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(options);

        var actor = target ?? new ActorContext();
        actor.Reset();

        var request = httpContext.Request;

        var subscriptionId = ReadHeader(request.Headers, options.SubscriptionIdHeader);
        var subscriptionName = ReadHeader(request.Headers, options.SubscriptionNameHeader);
        var clientId = ReadHeader(request.Headers, options.ClientIdHeader);
        var rawSubscriptionKey = ReadSubscriptionKey(request, options);
        var keyFingerprint = !string.IsNullOrWhiteSpace(rawSubscriptionKey)
            ? ComputeKeyFingerprint(rawSubscriptionKey)
            : null;

        var oauthPrincipal = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User
            : null;
        var oauthClientId = oauthPrincipal is not null
            ? FindFirstClaim(oauthPrincipal, OAuthClientIdClaimTypes)
            : null;
        var oauthName = oauthPrincipal is not null
            ? FindFirstClaim(oauthPrincipal, OAuthNameClaimTypes) ?? oauthPrincipal.Identity?.Name
            : null;

        var hasSubscriptionCredential = !string.IsNullOrWhiteSpace(subscriptionId) ||
                                        !string.IsNullOrWhiteSpace(clientId) ||
                                        !string.IsNullOrWhiteSpace(keyFingerprint);

        if (oauthPrincipal is null && !hasSubscriptionCredential)
        {
            return actor;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(keyFingerprint))
        {
            metadata["keyFingerprint"] = keyFingerprint;
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            metadata["clientId"] = clientId;
        }

        if (!string.IsNullOrWhiteSpace(oauthClientId))
        {
            metadata["oauthClientId"] = oauthClientId;
        }

        var mappedSurveyorName = LookupSurveyorName(
            options.SubscriptionKeySurveyorMap,
            subscriptionId,
            clientId,
            oauthClientId,
            keyFingerprint,
            rawSubscriptionKey);

        var resolvedName = FirstNonEmpty(subscriptionName, mappedSurveyorName, oauthName);
        var resolvedId = FirstNonEmpty(oauthClientId, subscriptionId, clientId, keyFingerprint) ?? "fauapi-actor";

        var authType = oauthPrincipal is not null && hasSubscriptionCredential
            ? "FaUApiOAuth2+SubscriptionKey"
            : oauthPrincipal is not null
                ? "FaUApiOAuth2"
                : "FaUApiSubscriptionKey";

        actor.SetActor(
            actorId: resolvedId,
            authenticationType: authType,
            actorName: resolvedName,
            subscriptionId: subscriptionId,
            subscriptionName: subscriptionName ?? mappedSurveyorName,
            metadata: metadata);

        return actor;
    }

    public static string ComputeKeyFingerprint(string rawSubscriptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawSubscriptionKey);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawSubscriptionKey.Trim()));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"fauapi-key:{hex[..12]}";
    }

    private static string? ReadSubscriptionKey(HttpRequest request, PublicAuthOptions options)
    {
        var headerVal = ReadHeader(request.Headers, options.SubscriptionKeyHeader);
        if (!string.IsNullOrWhiteSpace(headerVal))
        {
            return headerVal;
        }

        if (!string.IsNullOrWhiteSpace(options.SubscriptionKeyQueryParam) &&
            request.Query.TryGetValue(options.SubscriptionKeyQueryParam, out var queryVal) &&
            !string.IsNullOrWhiteSpace(queryVal))
        {
            return queryVal.ToString().Trim();
        }

        return null;
    }

    private static string? ReadHeader(IHeaderDictionary headers, string headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return null;
        }

        return headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString().Trim()
            : null;
    }

    private static string? LookupSurveyorName(
        IReadOnlyDictionary<string, string> map,
        params string?[] candidates)
    {
        if (map.Count == 0)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                map.TryGetValue(candidate, out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return null;
    }

    private static string? FindFirstClaim(ClaimsPrincipal principal, IEnumerable<string> claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var val = principal.Claims.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.Value) &&
                string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val.Trim();
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var val in values)
        {
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }

        return null;
    }
}
