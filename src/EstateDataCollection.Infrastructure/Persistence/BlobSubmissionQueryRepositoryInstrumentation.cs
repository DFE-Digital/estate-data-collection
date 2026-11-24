using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Infrastructure.Persistence;

public sealed partial class BlobSubmissionQueryRepositoryInstrumentation(
    ILogger<BlobSubmissionQueryRepository> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to list submissions from blob storage: {Error}"
    )]
    public partial void QueryListFailed(string error);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to load processed artefact for submission {SubmissionId}: {Error}"
    )]
    public partial void ProcessedLookupFailed(Guid submissionId, string error);
}
