using System.ComponentModel.DataAnnotations;

namespace EstateDataCollection.Infrastructure.Persistence;

public class BlobSubmissionRepositoryOptions : Common.Markers.IOption<BlobSubmissionRepositoryOptions>
{
    public static string SectionName
    {
        get { return "Blob:Submission"; }
    }

    [Required, MinLength(1)]
    public string PayloadContainerName { get; init; } = "payloads";

    [Required, MinLength(1)]
    public string ProcessedContainerName { get; init; } = "processed";

    public string SessionBlobNameFormat { get; init; } = "{submissionId}/session.json";
    public string PayloadBlobNameFormat { get; init; } = "{submissionId}/draft.json";

    [Required, MinLength(1)]
    public string DraftBlobPrefixFormat { get; init; } = "{submissionId}/drafts/";

    [Required, MinLength(1)]
    public string AuditBlobPrefixFormat { get; init; } = "{submissionId}/audit/";

    [Required, MinLength(1)]
    public string ProcessedBlobNameFormat { get; init; } = "urn={establishmentIdentifier}/schema={schemaName}/v={schemaVersion}/{submissionId}/payload.json";

    [Required, MinLength(1)]
    public string IdentifierIndexPrefix { get; init; } = "identifier-index/";

    [Required, MinLength(1)]
    public string ReferenceDataContainerName { get; init; } = "reference-data";
}
