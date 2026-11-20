using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public interface ISubmissionQueryRepository : IInfrastructure
{
    Task<Result<IReadOnlyList<SubmissionSummary>>> ListSubmissionsAsync(
        string? schemaName = null,
        int? schemaVersion = null,
        string? status = null,
        string? searchQuery = null,
        CancellationToken cancellationToken = default);

    Task<Result<SubmissionProcessedArtifact?>> GetProcessedArtifactAsync(
        SubmissionSession session,
        CancellationToken cancellationToken = default);
}
