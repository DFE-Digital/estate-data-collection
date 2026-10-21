using System.ComponentModel.DataAnnotations;

namespace EstateDataCollection.Infrastructure.Persistence;

public class BlobSubmissionRepositoryOptions : Common.Markers.IOption<BlobSubmissionRepositoryOptions>
{
    public static string SectionName => "Blob:Submission";
    
    [Required, MinLength(1)]
    public string PayloadContainerName { get; init; } = "payloads";
    
    [Required, MinLength(1)]
    public string ProcessedContainerName { get; init; } = "processed";
    
    public string SessionBlobNameFormat { get; init; } = "{submissionId}/session.json";
    public string PayloadBlobNameFormat { get; init; } = "{submissionId}/draft.json";
    
    [Required, MinLength(1)]
    public string ProcessedBlobNameFormat { get; init; } = "urn={urn}/schema={schemaName}/v={schemaVersion}/{submissionId}.json";
}