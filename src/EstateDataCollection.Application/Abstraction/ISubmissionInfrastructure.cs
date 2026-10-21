using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public interface ISubmissionInfrastructure : IInfrastructure
{
    Task<Result<Guid>> SaveSubmissionAsync(SubmissionEnvelope submission, CancellationToken cancellationToken = default);
    Task<Result<Guid>> CreateSessionAsync(SubmissionSession session, CancellationToken cancellationToken = default);
    Task<Result<SubmissionSession>> GetSessionAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<Result> SaveDraftPayloadAsync(Guid submissionId, int? urn, string rawPayloadJson, CancellationToken cancellationToken = default);
    Task<Result<string>> GetDraftPayloadAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<Result<string>> PromoteToProcessedAsync(Guid submissionId, int urn, string schemaName, int schemaVersion, CancellationToken cancellationToken = default);
}