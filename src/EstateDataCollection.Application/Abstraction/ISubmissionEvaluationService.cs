using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public interface ISubmissionEvaluationService : IApplication
{
    Task<SubmissionDraftInventory> LoadDraftInventoryAsync(
        Guid submissionId,
        SubmissionExecutionScope? scope = null,
        CancellationToken cancellationToken = default);

    Task<Result<string>> AssembleStoredDraftAsync(
        Guid submissionId,
        SubmissionDraftInventory inventory,
        SubmissionExecutionScope? scope = null,
        CancellationToken cancellationToken = default);

    Task<string?> TryAssembleDraftForDetailAsync(
        SubmissionSession session,
        SubmissionDraftInventory inventory,
        CancellationToken cancellationToken = default);

    Result<SubmissionCandidateDraft> AssembleCandidateDraft(
        SubmissionDraftInventory existingDrafts,
        string incomingPayload,
        SubmissionExecutionScope? scope = null);

    Task<Result<ResolvedSchema>> ResolveSchemaAsync(
        string schemaName,
        int schemaVersion,
        SubmissionExecutionScope? scope = null,
        CancellationToken cancellationToken = default);

    Task<Result<PayloadEvaluationResult>> EvaluateResolvedPayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        SubmissionSession session,
        bool isDraftUpdate,
        SubmissionExecutionScope? scope = null,
        CancellationToken cancellationToken = default);

    Task<Result<PayloadEvaluationResult>> EvaluatePayloadAsync(
        string rawPayloadJson,
        SubmissionSession session,
        bool isDraftUpdate,
        SubmissionExecutionScope? scope = null,
        CancellationToken cancellationToken = default);

    bool IsMissingDraftError(Error error);

    SubmissionAuditIdentifier? BuildAuditIdentifier(
        SubmissionSession session,
        SubmissionIdentifier? extractedIdentifier = null);
}
