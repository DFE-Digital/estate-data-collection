using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

internal interface IScopedPayloadValidationService
{
    Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate,
        SubmissionExecutionScope executionScope,
        CancellationToken cancellationToken = default);
}