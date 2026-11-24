using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public class FinaliseSubmissionUseCase(
    ISubmissionInfrastructure infrastructure,
    ISubmissionEvaluationService evaluationService,
    FinaliseSubmissionUseCaseInstrumentation? instrumentation = null,
    ISubmissionAuditStore? auditStore = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
) : IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest>
{
    private readonly FinaliseSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new FinaliseSubmissionUseCaseInstrumentation(NullLogger<FinaliseSubmissionUseCase>.Instance);

    public async Task<Result<FinaliseSubmissionResult>> ExecuteAsync(FinaliseSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.FinalisingSubmission(request.SubmissionId);

        using var scope = SubmissionExecutionScope.Start(
            "FinaliseSubmission",
            request.SubmissionId,
            processInstrumentation,
            auditStore,
            initialCapacity: 9);

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
            var alreadyErr = Error.Forbidden(ErrorCodes.SubmissionAlreadyFinalised);

            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                alreadyErr,
                outcomeStatus: "Rejected",
                identifier: evaluationService.BuildAuditIdentifier(session.Value),
                cancellationToken: cancellationToken);

            return alreadyErr;
        }

        var inventory = await evaluationService.LoadDraftInventoryAsync(request.SubmissionId, scope, cancellationToken);
        var draftPayload = await evaluationService.AssembleStoredDraftAsync(request.SubmissionId, inventory, scope, cancellationToken);

        if (draftPayload.IsFailure)
        {
            _instrumentation.DraftPayloadMissing(request.SubmissionId);

            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                draftPayload.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return draftPayload.Error;
        }

        var evaluation = await evaluationService.EvaluatePayloadAsync(
            draftPayload.Value,
            session.Value,
            isDraftUpdate: false,
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

        var validationErrors = evaluation.Value.ValidationErrors;
        if (validationErrors.Count > 0)
        {
            _instrumentation.ValidationFailed(request.SubmissionId, validationErrors.Count);
            var contractErr = Error.Validation(ErrorCodes.SchemaContractViolation, errors: validationErrors);

            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                contractErr,
                outcomeStatus: "Rejected",
                message: $"Finalisation rejected with {validationErrors.Count} validation error(s)",
                validationErrors: validationErrors,
                cancellationToken: cancellationToken);

            return contractErr;
        }

        var identifierEval = evaluation.Value.IdentifierEvaluation;
        if (!identifierEval.IsValid)
        {
            _instrumentation.InvalidUrn(request.SubmissionId, 0);
            var idErr = identifierEval.FailureError ?? Error.Validation(ErrorCodes.PayloadInvalidIdentifier);

            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                idErr,
                outcomeStatus: "Rejected",
                validationErrors: identifierEval.ValidationError is not null ? [identifierEval.ValidationError] : null,
                cancellationToken: cancellationToken);

            return idErr;
        }

        var extractedIdentifier = identifierEval.Identifier;
        var resolvedUrn = extractedIdentifier?.AsPositiveInt ?? 0;
        var auditIdentifier = evaluation.Value.AuditIdentifier;

        var isLegacyCompatibleUrnCall = extractedIdentifier is null ||
            (resolvedUrn > 0 && string.Equals(extractedIdentifier.Value.Type, SchemaIdentifierConfig.DefaultUrnType, StringComparison.OrdinalIgnoreCase));

        var promotion = await scope.ExecuteAsync(
            SubmissionProcessCatalog.PromoteToProcessed,
            () => isLegacyCompatibleUrnCall
                ? infrastructure.PromoteToProcessedAsync(
                    session.Value.SubmissionId,
                    resolvedUrn,
                    session.Value.SchemaName,
                    session.Value.SchemaVersion,
                    cancellationToken)
                : infrastructure.PromoteToProcessedAsync(
                    session.Value.SubmissionId,
                    extractedIdentifier!.Value.Type,
                    extractedIdentifier.Value.Value,
                    session.Value.SchemaName,
                    session.Value.SchemaVersion,
                    cancellationToken),
            _ => new ProcessStepDetails(Summary: "Promoted merged draft payload to processed container"));

        if (promotion.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                promotion.Error,
                outcomeStatus: "Failed",
                identifier: auditIdentifier,
                cancellationToken: cancellationToken);

            _instrumentation.SubmissionPromotionFailed(request.SubmissionId, promotion.Error.Description ?? string.Empty);
            return promotion.Error;
        }

        await scope.RecordAuditAsync(
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            "Succeeded",
            200,
            message: "Submission finalised and promoted to processed storage",
            identifier: auditIdentifier,
            details: new Dictionary<string, string>
            {
                ["processedBlobUri"] = promotion.Value
            },
            cancellationToken: cancellationToken);

        _instrumentation.SubmissionFinalisedAndPromoted(
            session.Value.SubmissionId,
            resolvedUrn,
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            promotion.Value);

        return new FinaliseSubmissionResult(
            session.Value.SubmissionId,
            resolvedUrn,
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            string.Empty,
            promotion.Value,
            DateTimeOffset.UtcNow,
            Identifier: extractedIdentifier?.Value,
            IdentifierType: extractedIdentifier?.Type
        );
    }
}
