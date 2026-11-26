using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public class UpdateSubmissionUseCase(
    ISubmissionInfrastructure infrastructure,
    ISubmissionEvaluationService evaluationService,
    UpdateSubmissionUseCaseInstrumentation? instrumentation = null,
    IIdentifierIndexRepository? identifierIndexRepository = null
) : IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest>
{
    private readonly UpdateSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new UpdateSubmissionUseCaseInstrumentation(NullLogger<UpdateSubmissionUseCase>.Instance);

    public async Task<Result<UpdateSubmissionResult>> ExecuteAsync(UpdateSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        _instrumentation.UpdatingDraftPayload(request.SubmissionId);

        var session = await SubmissionExecutionScope.StepAsync(
            SubmissionProcessCatalog.GetSession,
            () => infrastructure.GetSessionAsync(request.SubmissionId, cancellationToken),
            loaded => new ProcessStepDetails(
                Summary: loaded.IsFinalised ? SubmissionMessages.SessionAlreadyFinalised : SubmissionMessages.DraftSessionLoaded));

        if (session.IsFailure)
        {
            _instrumentation.SubmissionNotFound(request.SubmissionId);
            return session.Error;
        }

        SubmissionExecutionScope.Current?.SetSubmissionContext(
            session.Value,
            evaluationService.BuildAuditIdentifier(session.Value));

        if (session.Value.IsFinalised)
        {
            _instrumentation.SubmissionAlreadyFinalised(request.SubmissionId);
            return Error.Forbidden(ErrorCodes.SubmissionAlreadyFinalised);
        }

        var schema = await evaluationService.ResolveSchemaAsync(
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            cancellationToken);

        if (schema.IsFailure)
        {
            return schema.Error;
        }

        if (schema.Value.Metadata.IsRetired)
        {
            return Error.Validation(
                ErrorCodes.SchemaRetired,
                new
                {
                    schema.Value.Metadata.Name,
                    schema.Value.Metadata.Version,
                    Message = SubmissionMessages.SchemaRetiredNoUpdates(schema.Value.Metadata.Name, schema.Value.Metadata.Version)
                });
        }

        var inventory = await evaluationService.LoadDraftInventoryAsync(request.SubmissionId, cancellationToken);
        var candidateResult = evaluationService.AssembleCandidateDraft(inventory, request.Payload);
        if (candidateResult.IsFailure)
        {
            return candidateResult.Error;
        }

        var draftCountAfterSave = candidateResult.Value.DraftCountAfterSave;
        var evaluation = await evaluationService.EvaluateResolvedPayloadAsync(
            candidateResult.Value.MergedPayloadJson,
            schema.Value,
            session.Value,
            isDraftUpdate: true,
            cancellationToken);

        if (evaluation.IsFailure)
        {
            return evaluation.Error;
        }

        var errors = evaluation.Value.ValidationErrors;
        var auditIdentifier = evaluation.Value.AuditIdentifier;

        SubmissionExecutionScope.Current?.SetAuditContext(
            identifier: auditIdentifier,
            validationErrors: errors);

        var save = await SubmissionExecutionScope.StepAsync(
            SubmissionProcessCatalog.SaveDraftPayload,
            () => infrastructure.SaveDraftPayloadAsync(request.SubmissionId, request.Payload, cancellationToken),
            () => new ProcessStepDetails(
                ItemCount: draftCountAfterSave,
                Summary: SubmissionMessages.SavedDraftChunkSummary(draftCountAfterSave, request.Payload.Length)));

        if (save.IsFailure)
        {
            _instrumentation.DraftPayloadSaveFailed(request.SubmissionId, save.Error.Description ?? string.Empty);
            return save.Error;
        }

        if (identifierIndexRepository is not null &&
            evaluation.Value.IdentifierEvaluation.Identifier is { } extractedId &&
            !string.IsNullOrWhiteSpace(extractedId.Value))
        {
            var indexEntry = new IdentifierIndexEntry(
                SubmissionId: request.SubmissionId,
                SchemaName: session.Value.SchemaName,
                SchemaVersion: session.Value.SchemaVersion,
                Status: "Draft",
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                PayloadPath: $"{request.SubmissionId}/draft.json");

            await identifierIndexRepository.UpsertEntryAsync(
                extractedId.Type,
                extractedId.Value,
                indexEntry,
                cancellationToken);
        }

        var isValid = errors.Count == 0;

        if (!isValid)
        {
            _instrumentation.DraftValidationFailed(request.SubmissionId, errors.Count);
        }

        var guidance = SubmissionNextStepsBuilder.Build(
            session.Value.SubmissionId,
            isFinalised: false,
            finalisedAtUtc: null,
            hasDraftPayload: true,
            draftCount: draftCountAfterSave,
            pendingErrors: errors);

        SubmissionExecutionScope.Current?.SetAuditOutcome(
            outcomeStatus: isValid ? "Succeeded" : "SavedWithPendingErrors",
            httpStatus: 200,
            message: isValid
                ? SubmissionMessages.DraftSavedNoErrors(draftCountAfterSave)
                : SubmissionMessages.DraftSavedWithPendingErrors(draftCountAfterSave, errors.Count),
            validationErrors: errors,
            identifier: auditIdentifier);

        _instrumentation.DraftPayloadSaved(request.SubmissionId, isValid, errors.Count);

        return new UpdateSubmissionResult(
            SubmissionId: request.SubmissionId,
            IsSuccessful: isValid,
            PendingErrors: errors,
            SavedAtUtc: DateTimeOffset.UtcNow,
            DraftCount: draftCountAfterSave,
            NextSteps: guidance.NextSteps,
            Summary: guidance.Summary,
            Issues: evaluation.Value.EffectiveIssues
        );
    }
}
