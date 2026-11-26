using EstateDataCollection.Common.Markers;

namespace EstateDataCollection.AzureFunctions.Public.Options;

public sealed class PublicAuthOptions : IOption<PublicAuthOptions>
{
    public static string SectionName
    {
        get { return "Security:PublicAuth"; }
    }

    /// <summary>
    /// Enables worker-level enforcement that an identifiable FaUAPI actor (subscription key,
    /// forwarded APIM subscription identity header, or OAuth 2.0 principal) is present on
    /// authenticated public endpoints (`/api/schemas*` and `/api/submissions*`).
    /// Defaults to false so local `func start` and Azurite integration tests run without auth headers.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// When true and <see cref="Enabled"/> is true, requests without an identifiable actor on
    /// protected public endpoints are rejected with HTTP 401 (`application/problem+json`).
    /// </summary>
    public bool RequireActorOnAuthenticatedRoutes { get; init; } = true;

    /// <summary>
    /// Primary HTTP header carrying the FaUAPI / APIM subscription key.
    /// </summary>
    public string SubscriptionKeyHeader { get; init; } = "Ocp-Apim-Subscription-Key";

    /// <summary>
    /// Optional query parameter carrying the FaUAPI / APIM subscription key.
    /// </summary>
    public string SubscriptionKeyQueryParam { get; init; } = "subscription-key";

    /// <summary>
    /// HTTP header forwarded by FaUAPI / APIM inbound policy containing the APIM subscription ID.
    /// </summary>
    public string SubscriptionIdHeader { get; init; } = "X-FaUAPI-Subscription-Id";

    /// <summary>
    /// HTTP header forwarded by FaUAPI / APIM inbound policy containing the APIM subscription/surveyor name.
    /// </summary>
    public string SubscriptionNameHeader { get; init; } = "X-FaUAPI-Subscription-Name";

    /// <summary>
    /// HTTP header forwarded by FaUAPI / APIM inbound policy containing the OAuth client / consumer ID.
    /// </summary>
    public string ClientIdHeader { get; init; } = "X-FaUAPI-Client-Id";

    /// <summary>
    /// Optional lookup mapping a FaUAPI subscription ID, client ID, key fingerprint (`fauapi-key:&lt;hash&gt;`),
    /// or local dev subscription key to a friendly surveyor / organisation display name.
    /// Raw keys are never written to audit logs or telemetry.
    /// </summary>
    public Dictionary<string, string> SubscriptionKeySurveyorMap { get; init; } = new(StringComparer.Ordinal);
}
