using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public interface ISubmissionInfrastructure : IInfrastructure
{
    Task<Result<Guid>> CreateSessionAsync(SubmissionSession session, CancellationToken cancellationToken = default);
    Task<Result<SubmissionSession>> GetSessionAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<Result> SaveDraftPayloadAsync(Guid submissionId, string rawPayloadJson, CancellationToken cancellationToken = default);
    Task<Result<string>> GetDraftPayloadAsync(Guid submissionId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SubmissionDraftItem>>> GetDraftItemsAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyList<SubmissionDraftItem>>.Success(Array.Empty<SubmissionDraftItem>()));
    }

    Task<Result<string>> PromoteToProcessedAsync(
        Guid submissionId,
        string? identifierType,
        string? identifierValue,
        string schemaName,
        int schemaVersion,
        CancellationToken cancellationToken = default);
}
