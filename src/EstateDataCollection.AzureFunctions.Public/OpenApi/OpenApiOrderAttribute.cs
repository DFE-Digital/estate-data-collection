namespace EstateDataCollection.AzureFunctions.Public.OpenApi;

/// <summary>
/// Controls the display order of an OpenAPI endpoint within generated specs and Scalar UI
/// so operations flow in the intended integration sequence.
/// </summary>
/// <param name="order">Ascending sort key; lower values appear earlier.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class OpenApiOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
