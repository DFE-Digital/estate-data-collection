using EstateDataCollection.Common.Markers;

namespace EstateDataCollection.AzureFunctions.Public.Options;

public sealed class OpenApiDocsOptions : IOption<OpenApiDocsOptions>
{
    public static string SectionName
    {
        get { return "OpenApi:Docs"; }
    }

    /// <summary>
    /// Enables runtime `/api/openapi/{documentName}.json` and `.yaml` endpoints.
    /// Useful for Dev/Test and FaUAPI auto-import; can be disabled in Prod when using CI static specs.
    /// </summary>
    public bool EnableSpecEndpoints { get; init; } = true;

    /// <summary>
    /// Enables the interactive Scalar UI at `/api/scalar/`.
    /// When null (default), automatically enabled in non-Production environments and disabled in Production.
    /// </summary>
    public bool? EnableScalarUi { get; init; }

    /// <summary>
    /// Enables Scalar's interactive "Try it out" request runner.
    /// </summary>
    public bool EnableTryItOut { get; init; } = true;

    /// <summary>
    /// Target OpenAPI specification version ("3.0" or "3.1").
    /// Defaults to "3.0" for broadest Azure API Management / DfE FaUAPI compatibility.
    /// </summary>
    public string SpecVersion { get; init; } = "3.0";

    /// <summary>
    /// Optional DfE Find and Use an API (FaUAPI) gateway base URL added to the OpenAPI `servers` list
    /// (e.g. `https://api.education.gov.uk/estate-data-collection/v1`).
    /// </summary>
    public string? FaUApiGatewayUrl { get; init; }

    /// <summary>
    /// Optional DfE FaUAPI Native OAuth 2.0 `client_credentials` token endpoint URL.
    /// When configured alongside <see cref="FaUApiOAuthScope"/> (or <see cref="IncludeOAuth2SecurityScheme"/>),
    /// OAuth 2.0 is advertised as an alternative/additional FaUAPI security option alongside subscription keys.
    /// </summary>
    public string? FaUApiOAuthTokenUrl { get; init; }

    /// <summary>
    /// Optional OAuth 2.0 scope required by the FaUAPI product/subscription.
    /// </summary>
    public string? FaUApiOAuthScope { get; init; }

    /// <summary>
    /// Explicitly controls whether the `FaUApiOAuth2` scheme is emitted in OpenAPI `components.securitySchemes`.
    /// When null (default), emitted whenever <see cref="FaUApiOAuthTokenUrl"/> or <see cref="FaUApiOAuthScope"/> is set.
    /// </summary>
    public bool? IncludeOAuth2SecurityScheme { get; init; }

    /// <summary>
    /// When true and OAuth 2.0 is enabled, requires both Subscription Key AND OAuth 2.0 on protected operations.
    /// Defaults to false so FaUAPI Subscription Key works standalone initially (and OR'd with OAuth 2.0 when enabled).
    /// </summary>
    public bool RequireOAuth2WithSubscriptionKey { get; init; }

    /// <summary>
    /// Optional base URL for external API documentation / wiki pages (for example,
    /// `https://github.com/DFE-Digital/estate-data-collection/wiki`). When set, OpenAPI `externalDocs`
    /// links are emitted on the root document, tags, and decorated operations.
    /// </summary>
    public string? WikiBaseUrl { get; init; }

    /// <summary>
    /// Description shown alongside the root OpenAPI `externalDocs` link when <see cref="WikiBaseUrl"/> is configured.
    /// </summary>
    public string ExternalDocsDescription { get; init; } = "Estate Data Collection API Wiki & Integration Guides";
}
