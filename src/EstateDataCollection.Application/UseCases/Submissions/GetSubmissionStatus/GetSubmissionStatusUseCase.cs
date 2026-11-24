using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;

public class GetSubmissionStatusUseCase(
    ISubmissionInfrastructure infrastructure,
    ISubmissionEvaluationService evaluationService,
    GetSubmissionStatusUseCaseInstrumentation? instrumentation = null,
    ISubmissionAuditStore? auditStore = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
) : IUseCase<GetSubmissionStatusResult, GetSubmissionStatusRequest>
{
    private readonly GetSubmissionStatusUseCaseInstrumentation _instrumentation =
        instrumentation ?? new GetSubmissionStatusUseCaseInstrumentation(NullLogger<GetSubmissionStatusUseCase>.Instance);

    public async Task<Result<GetSubmissionStatusResult>> ExecuteAsync(
        GetSubmissionStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var actionName = string.IsNullOrWhiteSpace(request.ActionName)
            ? "CheckSubmissionStatus"
            : request.ActionName;

        _instrumentation.CheckingSubmissionStatus(request.SubmissionId);

        using var scope = SubmissionExecutionScope.Start(
            actionName,
            request.SubmissionId,
            processInstrumentation,
            auditStore,
            initialCapacity: 8);

        var sessionResult = await scope.ExecuteAsync(
            SubmissionProcessCatalog.GetSession,
            () => infrastructure.GetSessionAsync(request.SubmissionId, cancellationToken),
            loaded => new ProcessStepDetails(
                Summary: loaded.IsFinalised ? "Session finalised" : "Draft session loaded"));

        if (sessionResult.IsFailure)
        {
            _instrumentation.SubmissionNotFound(request.SubmissionId);
            return sessionResult.Error;
        }

        var session = sessionResult.Value;
        var inventory = await evaluationService.LoadDraftInventoryAsync(request.SubmissionId, scope, cancellationToken);

        if (session.IsFinalised)
        {
            var finalisedGuidance = SubmissionNextStepsBuilder.Build(
                session.SubmissionId,
                isFinalised: true,
                finalisedAtUtc: session.FinalisedAtUtc,
                hasDraftPayload: false,
                draftCount: inventory.Count,
                pendingErrors: []);

            await scope.RecordAuditAsync(
                session.SchemaName,
                session.SchemaVersion,
                "Finalised",
                200,
                message: finalisedGuidance.Summary,
                identifier: evaluationService.BuildAuditIdentifier(session),
                cancellationToken: cancellationToken);

            _instrumentation.SubmissionStatusChecked(
                session.SubmissionId,
                "Finalised",
                isReadyToFinalise: false,
                pendingErrorCount: 0);

            return new GetSubmissionStatusResult(
                SubmissionId: session.SubmissionId,
                Status: "Finalised",
                SchemaName: session.SchemaName,
                SchemaVersion: session.SchemaVersion,
                CreatedAtUtc: session.CreatedAtUtc,
                FinalisedAtUtc: session.FinalisedAtUtc,
                HasDraftPayload: false,
                IsReadyToFinalise: false,
                PendingErrors: [],
                Urn: session.Urn,
                Identifier: session.IdentifierValue,
                IdentifierType: session.IdentifierType,
                DraftCount: inventory.Count,
                LastDraftAtUtc: inventory.LastDraftAtUtc,
                NextSteps: finalisedGuidance.NextSteps,
                Summary: finalisedGuidance.Summary);
        }

        var draftPayload = await evaluationService.AssembleStoredDraftAsync(
            request.SubmissionId,
            inventory,
            scope,
            cancellationToken);

        if (draftPayload.IsFailure)
        {
            if (evaluationService.IsMissingDraftError(draftPayload.Error))
            {
                IReadOnlyList<ValidationError> missingDraftErrors =
                [
                    new ValidationError("$", "No draft payload has been uploaded yet.")
                ];

                var emptyGuidance = SubmissionNextStepsBuilder.Build(
                    session.SubmissionId,
                    isFinalised: false,
                    finalisedAtUtc: null,
                    hasDraftPayload: false,
                    draftCount: 0,
                    pendingErrors: missingDraftErrors);

                await scope.RecordAuditAsync(
                    session.SchemaName,
                    session.SchemaVersion,
                    "DraftAwaitingPayload",
                    200,
                    message: emptyGuidance.Summary,
                    validationErrors: missingDraftErrors,
                    cancellationToken: cancellationToken);

                _instrumentation.SubmissionStatusChecked(
                    session.SubmissionId,
                    "Draft",
                    isReadyToFinalise: false,
                    pendingErrorCount: missingDraftErrors.Count);

                return new GetSubmissionStatusResult(
                    SubmissionId: session.SubmissionId,
                    Status: "Draft",
                    SchemaName: session.SchemaName,
                    SchemaVersion: session.SchemaVersion,
                    CreatedAtUtc: session.CreatedAtUtc,
                    FinalisedAtUtc: null,
                    HasDraftPayload: false,
                    IsReadyToFinalise: false,
                    PendingErrors: missingDraftErrors,
                    Urn: session.Urn,
                    Identifier: session.IdentifierValue,
                    IdentifierType: session.IdentifierType,
                    DraftCount: 0,
                    LastDraftAtUtc: null,
                    NextSteps: emptyGuidance.NextSteps,
                    Summary: emptyGuidance.Summary);
            }

            await scope.RecordErrorAuditAsync(
                session.SchemaName,
                session.SchemaVersion,
                draftPayload.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return draftPayload.Error;
        }

        var effectiveDraftCount = Math.Max(1, inventory.Count);
        var evaluationResult = await evaluationService.EvaluatePayloadAsync(
            draftPayload.Value,
            session,
            isDraftUpdate: false,
            scope,
            cancellationToken);

        if (evaluationResult.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.SchemaName,
                session.SchemaVersion,
                evaluationResult.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return evaluationResult.Error;
        }

        var evaluation = evaluationResult.Value;
        var pendingErrors = evaluation.ValidationErrors;
        var isReadyToFinalise = pendingErrors.Count == 0;
        var resolvedIdentifier = evaluation.IdentifierEvaluation.Identifier?.Value ?? session.IdentifierValue;
        var resolvedIdentifierType = evaluation.IdentifierEvaluation.Identifier?.Type ?? session.IdentifierType;
        var resolvedUrn = evaluation.IdentifierEvaluation.Identifier?.AsPositiveInt ?? session.Urn;

        var draftGuidance = SubmissionNextStepsBuilder.Build(
            session.SubmissionId,
            isFinalised: false,
            finalisedAtUtc: null,
            hasDraftPayload: true,
            draftCount: effectiveDraftCount,
            pendingErrors: pendingErrors);

        await scope.RecordAuditAsync(
            session.SchemaName,
            session.SchemaVersion,
            isReadyToFinalise ? "ReadyToFinalise" : "DraftPendingErrors",
            200,
            message: draftGuidance.Summary,
            validationErrors: pendingErrors,
            identifier: evaluation.AuditIdentifier,
            cancellationToken: cancellationToken);

        _instrumentation.SubmissionStatusChecked(
            session.SubmissionId,
            "Draft",
            isReadyToFinalise,
            pendingErrors.Count);

        return new GetSubmissionStatusResult(
            SubmissionId: session.SubmissionId,
            Status: "Draft",
            SchemaName: session.SchemaName,
            SchemaVersion: session.SchemaVersion,
            CreatedAtUtc: session.CreatedAtUtc,
            FinalisedAtUtc: null,
            HasDraftPayload: true,
            IsReadyToFinalise: isReadyToFinalise,
            PendingErrors: pendingErrors,
            Urn: resolvedUrn,
            Identifier: resolvedIdentifier,
            IdentifierType: resolvedIdentifierType,
            DraftCount: effectiveDraftCount,
            LastDraftAtUtc: inventory.LastDraftAtUtc,
            NextSteps: draftGuidance.NextSteps,
            Summary: draftGuidance.Summary);
    }
}
