using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public class UpdateSubmissionUseCase(
    ISubmissionInfrastructure infrastructure,
    ISubmissionEvaluationService evaluationService,
    UpdateSubmissionUseCaseInstrumentation? instrumentation = null,
    ISubmissionAuditStore? auditStore = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
) : IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest>
{
    private readonly UpdateSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new UpdateSubmissionUseCaseInstrumentation(NullLogger<UpdateSubmissionUseCase>.Instance);

    public async Task<Result<UpdateSubmissionResult>> ExecuteAsync(UpdateSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        _instrumentation.UpdatingDraftPayload(request.SubmissionId);

        using var scope = SubmissionExecutionScope.Start(
            "UpdateSubmission",
            request.SubmissionId,
            processInstrumentation,
            auditStore,
            initialCapacity: 8);

        var session = await scope.ExecuteAsync(
            SubmissionProcessCatalog.GetSession,
            () => infrastructure.GetSessionAsync(request.SubmissionId, cancellationToken),
            loaded => new ProcessStepDetails(
                Summary: loaded.IsFinalised ? "Session already finalised" : "Draft session loaded"));

        if (session.IsFailure)
        {
            _instrumentation.SubmissionNotFound(request.SubmissionId);
            return session.Error;
        }

        if (session.Value.IsFinalised)
        {
            _instrumentation.SubmissionAlreadyFinalised(request.SubmissionId);
            var finalisedErr = Error.Forbidden(ErrorCodes.SubmissionAlreadyFinalised);

            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                finalisedErr,
                outcomeStatus: "Rejected",
                identifier: evaluationService.BuildAuditIdentifier(session.Value),
                cancellationToken: cancellationToken);

            return finalisedErr;
        }

        var schema = await evaluationService.ResolveSchemaAsync(
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            scope,
            cancellationToken);

        if (schema.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                schema.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return schema.Error;
        }

        var inventory = await evaluationService.LoadDraftInventoryAsync(request.SubmissionId, scope, cancellationToken);
        var candidateResult = evaluationService.AssembleCandidateDraft(inventory, request.Payload, scope);
        if (candidateResult.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                candidateResult.Error,
                outcomeStatus: "Rejected",
                cancellationToken: cancellationToken);

            return candidateResult.Error;
        }

        var draftCountAfterSave = candidateResult.Value.DraftCountAfterSave;
        var evaluation = await evaluationService.EvaluateResolvedPayloadAsync(
            candidateResult.Value.MergedPayloadJson,
            schema.Value,
            session.Value,
            isDraftUpdate: true,
            scope,
            cancellationToken);

        if (evaluation.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                evaluation.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return evaluation.Error;
        }

        var errors = evaluation.Value.ValidationErrors;
        var auditIdentifier = evaluation.Value.AuditIdentifier;

        var save = await scope.ExecuteAsync(
            SubmissionProcessCatalog.SaveDraftPayload,
            () => infrastructure.SaveDraftPayloadAsync(request.SubmissionId, null, request.Payload, cancellationToken),
            () => new ProcessStepDetails(
                ItemCount: draftCountAfterSave,
                Summary: $"Saved timestamped draft chunk #{draftCountAfterSave} ({request.Payload.Length} chars)"));

        if (save.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                save.Error,
                outcomeStatus: "Failed",
                validationErrors: errors,
                identifier: auditIdentifier,
                cancellationToken: cancellationToken);

            _instrumentation.DraftPayloadSaveFailed(request.SubmissionId, save.Error.Description ?? string.Empty);
            return save.Error;
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

        await scope.RecordAuditAsync(
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            isValid ? "Succeeded" : "SavedWithPendingErrors",
            200,
            message: isValid
                ? $"Draft payload saved (chunk #{draftCountAfterSave}) with no validation errors"
                : $"Draft payload saved (chunk #{draftCountAfterSave}) with {errors.Count} pending validation error(s)",
            validationErrors: errors,
            identifier: auditIdentifier,
            cancellationToken: cancellationToken);

        _instrumentation.DraftPayloadSaved(request.SubmissionId, isValid, errors.Count);

        return new UpdateSubmissionResult(
            SubmissionId: request.SubmissionId,
            IsSuccessful: isValid,
            PendingErrors: errors,
            SavedAtUtc: DateTimeOffset.UtcNow,
            DraftCount: draftCountAfterSave,
            NextSteps: guidance.NextSteps,
            Summary: guidance.Summary
        );
    }
}
