using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public class FinaliseSubmissionUseCase(
    ISubmissionInfrastructure infrastructure,
    ISubmissionEvaluationService evaluationService,
    FinaliseSubmissionUseCaseInstrumentation? instrumentation = null
) : IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest>
{
    private readonly FinaliseSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new FinaliseSubmissionUseCaseInstrumentation(NullLogger<FinaliseSubmissionUseCase>.Instance);

    public async Task<Result<FinaliseSubmissionResult>> ExecuteAsync(FinaliseSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.FinalisingSubmission(request.SubmissionId);

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

        var inventory = await evaluationService.LoadDraftInventoryAsync(request.SubmissionId, cancellationToken);
        var draftPayload = await evaluationService.AssembleStoredDraftAsync(request.SubmissionId, inventory, cancellationToken);

        if (draftPayload.IsFailure)
        {
            _instrumentation.DraftPayloadMissing(request.SubmissionId);
            return draftPayload.Error;
        }

        var evaluation = await evaluationService.EvaluatePayloadAsync(
            draftPayload.Value,
            session.Value,
            isDraftUpdate: false,
            cancellationToken);

        if (evaluation.IsFailure)
        {
            return evaluation.Error;
        }

        if (evaluation.Value.Schema.Metadata.IsRetired)
        {
            return Error.Validation(
                ErrorCodes.SchemaRetired,
                new
                {
                    evaluation.Value.Schema.Metadata.Name,
                    evaluation.Value.Schema.Metadata.Version,
                    Message = SubmissionMessages.SchemaRetiredNoFinalisation(evaluation.Value.Schema.Metadata.Name, evaluation.Value.Schema.Metadata.Version)
                });
        }

        var validationErrors = evaluation.Value.ValidationErrors;
        if (validationErrors.Count > 0)
        {
            _instrumentation.ValidationFailed(request.SubmissionId, validationErrors.Count);
            SubmissionExecutionScope.Current?.SetAuditContext(
                validationErrors: validationErrors,
                message: SubmissionMessages.FinalisationRejectedSummary(validationErrors.Count));

            return Error.Validation(ErrorCodes.SchemaContractViolation, errors: validationErrors);
        }

        var identifierEval = evaluation.Value.IdentifierEvaluation;
        if (!identifierEval.IsValid)
        {
            var reason = identifierEval.ValidationError?.Message
                         ?? identifierEval.FailureError?.Description
                         ?? identifierEval.FailureError?.Code
                         ?? SubmissionMessages.IdentifierInvalidFallback;
            _instrumentation.InvalidIdentifier(request.SubmissionId, reason);
            if (identifierEval.ValidationError is not null)
            {
                SubmissionExecutionScope.Current?.SetAuditContext(validationErrors: [identifierEval.ValidationError]);
            }

            return identifierEval.FailureError ?? Error.Validation(ErrorCodes.PayloadInvalidIdentifier);
        }

        var extractedIdentifier = identifierEval.Identifier;
        var auditIdentifier = evaluation.Value.AuditIdentifier;

        SubmissionExecutionScope.Current?.SetAuditContext(identifier: auditIdentifier);

        var promotion = await SubmissionExecutionScope.StepAsync(
            SubmissionProcessCatalog.PromoteToProcessed,
            () => infrastructure.PromoteToProcessedAsync(
                session.Value.SubmissionId,
                extractedIdentifier?.Type,
                extractedIdentifier?.Value,
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                cancellationToken),
            _ => new ProcessStepDetails(Summary: SubmissionMessages.PromotedMergedDraftSummary));

        if (promotion.IsFailure)
        {
            _instrumentation.SubmissionPromotionFailed(request.SubmissionId, promotion.Error.Description ?? string.Empty);
            return promotion.Error;
        }

        SubmissionExecutionScope.Current?.SetAuditOutcome(
            outcomeStatus: "Succeeded",
            httpStatus: 200,
            message: SubmissionMessages.SubmissionFinalisedAuditMessage,
            identifier: auditIdentifier,
            details: new Dictionary<string, string>
            {
                ["processedBlobUri"] = promotion.Value
            });

        _instrumentation.SubmissionFinalisedAndPromoted(
            session.Value.SubmissionId,
            extractedIdentifier?.Type ?? "none",
            extractedIdentifier?.Value ?? "unassigned",
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            promotion.Value);

        return new FinaliseSubmissionResult(
            session.Value.SubmissionId,
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            "Finalised",
            promotion.Value,
            DateTimeOffset.UtcNow,
            Identifier: extractedIdentifier?.Value,
            IdentifierType: extractedIdentifier?.Type,
            Issues: evaluation.Value.EffectiveIssues
        );
    }
}
