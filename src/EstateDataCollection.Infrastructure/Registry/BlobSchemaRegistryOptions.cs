using System.ComponentModel.DataAnnotations;

namespace EstateDataCollection.Infrastructure.Registry;

public class BlobSchemaRegistryOptions : Common.Markers.IOption<BlobSchemaRegistryOptions>
{
    public static string SectionName => "Blob:Schema";

    [Required, MinLength(1)]
    public string ContainerName { get; init; } = "schemas";

    /// <summary>
    /// Staging/draft container where raw .schema.json, .rules.json, or .odcs.yaml files are dropped
    /// and ingested asynchronously via Azure Event Grid (Microsoft.Storage.BlobCreated).
    /// </summary>
    [Required, MinLength(1)]
    public string DraftContainerName { get; init; } = "draft-schemas";

    /// <summary>
    /// Suffix appended to a draft blob name when writing validation failure diagnostics back to the draft container.
    /// </summary>
    [Required, MinLength(1)]
    public string DraftErrorBlobSuffix { get; init; } = ".error.json";
    
    [Required, MinLength(1)]
    public string CacheKey { get; init; } = "schema:{schemaName}:v{version}";
    
    [Required, MinLength(1)]
    public string SchemaBlobDirectory { get; init; } = "{schemaName}/v{version}.schema.json";
    
    [Required, MinLength(1)]
    public string MetaBlobDirectory { get; init; } = "{schemaName}/v{version}.meta.json";

    /// <summary>
    /// Blob path format for companion quality/business rules JSON ({schemaName}/v{version}.rules.json).
    /// </summary>
    [Required, MinLength(1)]
    public string RulesBlobDirectory { get; init; } = "{schemaName}/v{version}.rules.json";

    /// <summary>
    /// Legacy alias for RulesBlobDirectory ({schemaName}/v{version}.logic.json) checked as fallback on read.
    /// </summary>
    [Required, MinLength(1)]
    public string LogicBlobDirectory { get; init; } = "{schemaName}/v{version}.logic.json";

    /// <summary>
    /// Blob path format when raw ODCS YAML contracts are uploaded directly ({schemaName}/v{version}.odcs.yaml).
    /// </summary>
    [Required, MinLength(1)]
    public string OdcsBlobDirectory { get; init; } = "{schemaName}/v{version}.odcs.yaml";
}
