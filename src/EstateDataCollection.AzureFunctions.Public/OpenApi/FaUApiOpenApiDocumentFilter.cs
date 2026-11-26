using System.Reflection;
using EstateDataCollection.AzureFunctions.Public.Options;
using EstateDataCollection.Common.Web.Security;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EstateDataCollection.AzureFunctions.Public.OpenApi;

/// <summary>
/// Enriches the generated OpenAPI document for DfE Find and Use an API (FaUAPI) ingestion:
/// - Configures server URLs (`/api` for direct Function host + optional FaUAPI gateway URL)
/// - Orders tags and operations in end-to-end integration workflow order (`Schema Discovery` -&gt; `Submissions` -&gt; `Diagnostics`)
/// - Registers FaUAPI subscription-key (`Ocp-Apim-Subscription-Key`) as the primary security scheme,
///   with optional OAuth 2.0 `client_credentials` support when configured
/// - Leaves `[AllowAnonymous]` operations unauthenticated while marking protected endpoints
/// - Populates root and tag `externalDocs` links when `OpenApi:Docs:WikiBaseUrl` is configured
/// - Removes unused `CancellationToken` / `WaitHandle` schemas leaked by Azure Functions method signatures
/// </summary>
public sealed class FaUApiOpenApiDocumentFilter(
    OpenApiDocsOptions docsOptions
) : IDocumentFilter
{
    public const string SubscriptionKeySchemeId = "FaUApiSubscriptionKey";
    public const string OAuth2SchemeId = "FaUApiOAuth2";

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Servers ??= [];
        swaggerDoc.Servers.Clear();

        if (!string.IsNullOrWhiteSpace(docsOptions.FaUApiGatewayUrl))
        {
            swaggerDoc.Servers.Add(new OpenApiServer
            {
                Url = docsOptions.FaUApiGatewayUrl.TrimEnd('/'),
                Description = "DfE Find and Use an API (FaUAPI) Gateway"
            });
        }

        swaggerDoc.Servers.Add(new OpenApiServer
        {
            Url = "/api",
            Description = "Azure Functions Public Host"
        });

        var rootWikiUri = OpenApiOverviewLoader.TryBuildWikiUri(docsOptions.WikiBaseUrl);
        if (rootWikiUri is not null)
        {
            swaggerDoc.ExternalDocs = new OpenApiExternalDocs
            {
                Url = rootWikiUri,
                Description = docsOptions.ExternalDocsDescription
            };
        }

        swaggerDoc.Tags = new HashSet<OpenApiTag>
        {
            CreateTag(
                "Schema Discovery",
                "Step 1: Discover active, upcoming, and retired schema contracts before submitting data.",
                "Schema-Discovery",
                "Schema discovery and contract inspection guide"),
            CreateTag(
                "Submissions",
                "Step 2: Create a submission session, upload drafts, check validation readiness, and finalise.",
                "Submissions-Lifecycle",
                "Submission lifecycle, draft merging, and validation guide"),
            CreateTag(
                "Diagnostics",
                "Service health and build version metadata.",
                "Public-API-Health",
                "Health and diagnostics guide")
        };

        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        swaggerDoc.Components.SecuritySchemes[SubscriptionKeySchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = "Ocp-Apim-Subscription-Key",
            In = ParameterLocation.Header,
            Description = "DfE Find and Use an API (FaUAPI) subscription key identifying the calling surveyor/organisation."
        };

        var includeOAuth2 = docsOptions.IncludeOAuth2SecurityScheme ??
                            (!string.IsNullOrWhiteSpace(docsOptions.FaUApiOAuthTokenUrl) ||
                             !string.IsNullOrWhiteSpace(docsOptions.FaUApiOAuthScope));

        var scopes = new Dictionary<string, string>();
        if (includeOAuth2)
        {
            var tokenUrl = string.IsNullOrWhiteSpace(docsOptions.FaUApiOAuthTokenUrl)
                ? "https://login.education.gov.uk/oauth2/v2.0/token"
                : docsOptions.FaUApiOAuthTokenUrl.Trim();

            if (!string.IsNullOrWhiteSpace(docsOptions.FaUApiOAuthScope))
            {
                scopes[docsOptions.FaUApiOAuthScope.Trim()] = "Access Estate Data Collection Public API";
            }

            swaggerDoc.Components.SecuritySchemes[OAuth2SchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "DfE FaUAPI Native OAuth 2.0 Application-flow (client_credentials).",
                Flows = new OpenApiOAuthFlows
                {
                    ClientCredentials = new OpenApiOAuthFlow
                    {
                        TokenUrl = new Uri(tokenUrl),
                        Scopes = scopes
                    }
                }
            };
        }

        if (swaggerDoc.Components.Schemas is not null)
        {
            var leakedKeys = swaggerDoc.Components.Schemas.Keys
                .Where(k =>
                    k.Contains("CancellationToken", StringComparison.Ordinal) ||
                    k.Contains("WaitHandle", StringComparison.Ordinal))
                .ToList();

            foreach (var key in leakedKeys)
            {
                swaggerDoc.Components.Schemas.Remove(key);
            }
        }

        if (swaggerDoc.Paths is null)
        {
            return;
        }

        var anonymousOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operationOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var apiDesc in context.ApiDescriptions)
        {
            var operationKey = BuildOperationKey(apiDesc.HttpMethod, apiDesc.RelativePath);
            if (apiDesc.TryGetMethodInfo(out var methodInfo))
            {
                if (FunctionAuthMetadataResolver.IsAllowAnonymous(methodInfo.DeclaringType, methodInfo))
                {
                    anonymousOperations.Add(operationKey);
                }

                var order = methodInfo.GetCustomAttribute<OpenApiOrderAttribute>()?.Order
                            ?? methodInfo.DeclaringType?.GetCustomAttribute<OpenApiOrderAttribute>()?.Order;
                if (order.HasValue)
                {
                    operationOrders[operationKey] = order.Value;
                }
            }
        }

        foreach (var (pathKey, pathItem) in swaggerDoc.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            foreach (var (httpMethod, operation) in pathItem.Operations)
            {
                if (anonymousOperations.Contains(BuildOperationKey(httpMethod.ToString(), pathKey)))
                {
                    operation.Security = [];
                    continue;
                }

                operation.Security ??= [];
                operation.Security.Clear();

                if (includeOAuth2 && docsOptions.RequireOAuth2WithSubscriptionKey)
                {
                    // Combined security requirement (AND): Subscription key + OAuth 2.0 Bearer token
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(SubscriptionKeySchemeId, swaggerDoc)] = [],
                        [new OpenApiSecuritySchemeReference(OAuth2SchemeId, swaggerDoc)] = scopes.Keys.ToList()
                    });
                }
                else
                {
                    // Subscription key standalone works immediately
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(SubscriptionKeySchemeId, swaggerDoc)] = []
                    });

                    // When OAuth 2.0 is also configured, list (Subscription key + OAuth 2.0) as an alternative requirement (OR)
                    if (includeOAuth2)
                    {
                        operation.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(SubscriptionKeySchemeId, swaggerDoc)] = [],
                            [new OpenApiSecuritySchemeReference(OAuth2SchemeId, swaggerDoc)] = scopes.Keys.ToList()
                        });
                    }
                }
            }

            if (pathItem.Operations.Count > 1)
            {
                var orderedOps = pathItem.Operations
                    .OrderBy(kvp => operationOrders.GetValueOrDefault(BuildOperationKey(kvp.Key.ToString(), pathKey), int.MaxValue))
                    .ThenBy(kvp => kvp.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                pathItem.Operations.Clear();
                foreach (var (method, op) in orderedOps)
                {
                    pathItem.Operations.Add(method, op);
                }
            }
        }

        var orderedPaths = swaggerDoc.Paths
            .OrderBy(kvp =>
            {
                if (kvp.Value.Operations is null || kvp.Value.Operations.Count == 0)
                {
                    return int.MaxValue;
                }

                return kvp.Value.Operations.Min(op =>
                    operationOrders.GetValueOrDefault(BuildOperationKey(op.Key.ToString(), kvp.Key), int.MaxValue));
            })
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        swaggerDoc.Paths.Clear();
        foreach (var (pathKey, pathItem) in orderedPaths)
        {
            swaggerDoc.Paths.Add(pathKey, pathItem);
        }
    }

    private OpenApiTag CreateTag(string name, string description, string wikiSlug, string wikiDescription)
    {
        var tag = new OpenApiTag
        {
            Name = name,
            Description = description
        };

        var tagWikiUri = OpenApiOverviewLoader.TryBuildWikiUri(docsOptions.WikiBaseUrl, wikiSlug);
        if (tagWikiUri is not null)
        {
            tag.ExternalDocs = new OpenApiExternalDocs
            {
                Url = tagWikiUri,
                Description = wikiDescription
            };
        }

        return tag;
    }

    private static string BuildOperationKey(string? httpMethod, string? relativePath)
    {
        var method = string.IsNullOrWhiteSpace(httpMethod) ? "GET" : httpMethod.Trim();
        var path = "/" + (relativePath ?? string.Empty).TrimStart('/');
        return $"{method}:{path}";
    }
}
