using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public interface ISubmissionAuditStore : IInfrastructure
{
    Task<Result<string>> AppendAsync(SubmissionAuditEntry entry, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SubmissionAuditEntry>>> ListAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default);
}
