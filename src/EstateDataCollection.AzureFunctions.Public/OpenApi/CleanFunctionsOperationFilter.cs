using System.Reflection;
using EstateDataCollection.AzureFunctions.Public.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EstateDataCollection.AzureFunctions.Public.OpenApi;

/// <summary>
/// Cleans Azure Functions isolated-worker artifacts from generated OpenAPI operations:
/// - Strips synthetic `CancellationToken` request bodies emitted by `AzureExtensions.Swashbuckle`
/// - Normalises operation tags into FaUAPI-friendly domains (`Diagnostics`, `Schema Discovery`, `Submissions`)
/// - Populates operation `externalDocs` links from `[OpenApiWiki]` when `OpenApi:Docs:WikiBaseUrl` is configured
/// </summary>
public sealed class CleanFunctionsOperationFilter(OpenApiDocsOptions docsOptions) : IOperationFilter
{
    public CleanFunctionsOperationFilter() : this(new OpenApiDocsOptions())
    {
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasCancellationTokenBody = context.ApiDescription.ParameterDescriptions.Any(p =>
            p.Type == typeof(CancellationToken) ||
            p.ModelMetadata?.ModelType == typeof(CancellationToken));

        var hasExplicitRequestBody = context.MethodInfo
            .GetParameters()
            .Any(p => p.GetCustomAttributes(inherit: true)
                .Any(a => a.GetType().Name.Contains("RequestBodyType", StringComparison.Ordinal)));

        if (hasCancellationTokenBody && !hasExplicitRequestBody)
        {
            operation.RequestBody = null;
        }

        var route = context.ApiDescription.RelativePath ?? string.Empty;
        var tagName = route switch
        {
            var r when r.Contains("schemas", StringComparison.OrdinalIgnoreCase) => "Schema Discovery",
            var r when r.Contains("submissions", StringComparison.OrdinalIgnoreCase) => "Submissions",
            _ => "Diagnostics"
        };

        operation.Tags = new HashSet<OpenApiTagReference>
        {
            new(tagName)
        };

        var wikiAttribute = context.MethodInfo.GetCustomAttribute<OpenApiWikiAttribute>()
                            ?? context.MethodInfo.DeclaringType?.GetCustomAttribute<OpenApiWikiAttribute>();
        if (wikiAttribute is not null)
        {
            var wikiUri = OpenApiOverviewLoader.TryBuildWikiUri(docsOptions.WikiBaseUrl, wikiAttribute.Slug);
            if (wikiUri is not null)
            {
                operation.ExternalDocs = new OpenApiExternalDocs
                {
                    Url = wikiUri,
                    Description = wikiAttribute.Description ?? operation.Summary ?? $"{tagName} guide"
                };
            }
        }
    }
}
