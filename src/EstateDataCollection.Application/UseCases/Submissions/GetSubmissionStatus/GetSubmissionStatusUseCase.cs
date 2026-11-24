using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;

public class GetSubmissionStatusUseCase(
    ISubmissionInfrastructure infrastructure,
    ISubmissionEvaluationService evaluationService,
    GetSubmissionStatusUseCaseInstrumentation? instrumentation = null
) : IUseCase<GetSubmissionStatusResult, GetSubmissionStatusRequest>
{
    private readonly GetSubmissionStatusUseCaseInstrumentation _instrumentation =
        instrumentation ?? new GetSubmissionStatusUseCaseInstrumentation(NullLogger<GetSubmissionStatusUseCase>.Instance);

    public async Task<Result<GetSubmissionStatusResult>> ExecuteAsync(
        GetSubmissionStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.CheckingSubmissionStatus(request.SubmissionId);

        var sessionResult = await SubmissionExecutionScope.StepAsync(
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
        SubmissionExecutionScope.Current?.SetSubmissionContext(
            session,
            evaluationService.BuildAuditIdentifier(session));

        var inventory = await evaluationService.LoadDraftInventoryAsync(request.SubmissionId, cancellationToken);

        if (session.IsFinalised)
        {
            var finalisedGuidance = SubmissionNextStepsBuilder.Build(
                session.SubmissionId,
                isFinalised: true,
                finalisedAtUtc: session.FinalisedAtUtc,
                hasDraftPayload: false,
                draftCount: inventory.Count,
                pendingErrors: []);

            SubmissionExecutionScope.Current?.SetAuditOutcome(
                outcomeStatus: "Finalised",
                httpStatus: 200,
                message: finalisedGuidance.Summary,
                identifier: evaluationService.BuildAuditIdentifier(session));

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

                SubmissionExecutionScope.Current?.SetAuditOutcome(
                    outcomeStatus: "DraftAwaitingPayload",
                    httpStatus: 200,
                    message: emptyGuidance.Summary,
                    validationErrors: missingDraftErrors);

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
                    Identifier: session.IdentifierValue,
                    IdentifierType: session.IdentifierType,
                    DraftCount: 0,
                    LastDraftAtUtc: null,
                    NextSteps: emptyGuidance.NextSteps,
                    Summary: emptyGuidance.Summary);
            }

            return draftPayload.Error;
        }

        var effectiveDraftCount = Math.Max(1, inventory.Count);
        var evaluationResult = await evaluationService.EvaluatePayloadAsync(
            draftPayload.Value,
            session,
            isDraftUpdate: false,
            cancellationToken);

        if (evaluationResult.IsFailure)
        {
            return evaluationResult.Error;
        }

        var evaluation = evaluationResult.Value;
        var pendingErrors = evaluation.ValidationErrors;
        var isReadyToFinalise = pendingErrors.Count == 0;
        var resolvedIdentifier = evaluation.IdentifierEvaluation.Identifier?.Value ?? session.IdentifierValue;
        var resolvedIdentifierType = evaluation.IdentifierEvaluation.Identifier?.Type ?? session.IdentifierType;

        var draftGuidance = SubmissionNextStepsBuilder.Build(
            session.SubmissionId,
            isFinalised: false,
            finalisedAtUtc: null,
            hasDraftPayload: true,
            draftCount: effectiveDraftCount,
            pendingErrors: pendingErrors);

        SubmissionExecutionScope.Current?.SetAuditOutcome(
            outcomeStatus: isReadyToFinalise ? "ReadyToFinalise" : "DraftPendingErrors",
            httpStatus: 200,
            message: draftGuidance.Summary,
            validationErrors: pendingErrors,
            identifier: evaluation.AuditIdentifier);

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
            Identifier: resolvedIdentifier,
            IdentifierType: resolvedIdentifierType,
            DraftCount: effectiveDraftCount,
            LastDraftAtUtc: inventory.LastDraftAtUtc,
            NextSteps: draftGuidance.NextSteps,
            Summary: draftGuidance.Summary);
    }
}
