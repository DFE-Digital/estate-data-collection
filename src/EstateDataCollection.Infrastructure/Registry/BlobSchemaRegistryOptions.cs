using System.ComponentModel.DataAnnotations;

namespace EstateDataCollection.Infrastructure.Registry;

public class BlobSchemaRegistryOptions : Common.Markers.IOption<BlobSchemaRegistryOptions>
{
    public static string SectionName => "Blob:Schema";
    
    [Required, MinLength(1)]
    public string ContainerName { get; init; } = "schemas";
    
    [Required, MinLength(1)]
    public string CacheKey { get; init; } = "schema:{schemaName}:v{version}";
    
    [Required, MinLength(1)]
    public string SchemaBlobDirectory { get; init; } = "{schemaName}/v{version}.schema.json";
    
    [Required, MinLength(1)]
    public string MetaBlobDirectory { get; init; } = "{schemaName}/v{version}.meta.json";
}