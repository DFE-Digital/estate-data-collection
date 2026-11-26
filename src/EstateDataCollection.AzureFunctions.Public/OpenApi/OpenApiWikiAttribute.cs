namespace EstateDataCollection.AzureFunctions.Public.OpenApi;

/// <summary>
/// Associates an OpenAPI operation or endpoint class with an external wiki page slug
/// (resolved relative to <c>OpenApi:Docs:WikiBaseUrl</c> when configured).
/// </summary>
/// <param name="slug">Wiki page slug (e.g. `Submissions-Lifecycle`) or full absolute URL.</param>
/// <param name="description">Optional human-readable label for the OpenAPI `externalDocs.description` field.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class OpenApiWikiAttribute(string slug, string? description = null) : Attribute
{
    public string Slug { get; } = slug;

    public string? Description { get; } = description;
}
