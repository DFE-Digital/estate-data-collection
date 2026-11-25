namespace EstateDataCollection.Domain.Submission;

/// <summary>
/// Canonical process step definitions recorded in <see cref="SubmissionAuditEntry.ProcessesConsumed"/>
/// and OpenTelemetry spans/metrics.
/// </summary>
public static class SubmissionProcessCatalog
{
    public static readonly SubmissionProcessDescriptor SchemaResolution =
        new("SchemaResolution", "schema-registry");

    public static readonly SubmissionProcessDescriptor GetSession =
        new("BlobStorage:GetSession", "azure-blob");

    public static readonly SubmissionProcessDescriptor CreateSession =
        new("BlobStorage:CreateSession", "azure-blob");

    public static readonly SubmissionProcessDescriptor GetDraftItems =
        new("BlobStorage:GetDraftItems", "azure-blob");

    public static readonly SubmissionProcessDescriptor GetDraftPayload =
        new("BlobStorage:GetDraftPayload", "azure-blob");

    public static readonly SubmissionProcessDescriptor SaveDraftPayload =
        new("BlobStorage:SaveDraftPayload", "azure-blob");

    public static readonly SubmissionProcessDescriptor PromoteToProcessed =
        new("BlobStorage:PromoteToProcessed", "azure-blob");

    public static readonly SubmissionProcessDescriptor DraftPayloadMerger =
        new("DraftPayloadMerger", "rfc7396-json-merge");

    public static readonly SubmissionProcessDescriptor JsonSchemaValidator =
        new("JsonSchemaValidator", "json-schema");

    public static readonly SubmissionProcessDescriptor QualityRulesValidator =
        new("QualityRulesValidator", "rules-engine");

    public static readonly SubmissionProcessDescriptor IdentifierExtractor =
        new("IdentifierExtractor", "identifier-extractor");

    public static readonly SubmissionProcessDescriptor CrossSubmissionEnrichment =
        new("CrossSubmissionEnrichment", "cross-submission-enrichment");

    public static readonly SubmissionProcessDescriptor ReferenceDataEnrichment =
        new("ReferenceDataEnrichment", "reference-data-enrichment");
}
