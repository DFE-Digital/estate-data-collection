using System.Text.Json;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;

public sealed class GetSubmissionDetailUseCase(
    ISubmissionInfrastructure submissionInfrastructure,
    ISubmissionQueryRepository queryRepository,
    ISubmissionAuditStore auditStore,
    ISubmissionEvaluationService evaluationService
) : IUseCase<GetSubmissionDetailResult, GetSubmissionDetailRequest>
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<Result<GetSubmissionDetailResult>> ExecuteAsync(
        GetSubmissionDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = await submissionInfrastructure.GetSessionAsync(request.SubmissionId, cancellationToken);
        if (sessionResult.IsFailure)
        {
            return sessionResult.Error;
        }

        var session = sessionResult.Value;
        var rawSessionJson = JsonSerializer.Serialize(session, PrettyJsonOptions);

        var inventory = await evaluationService.LoadDraftInventoryAsync(request.SubmissionId, cancellationToken);
        var mergedDraftPayload = await evaluationService.TryAssembleDraftForDetailAsync(session, inventory, cancellationToken);

        SubmissionProcessedArtifact? processedArtifact = null;
        if (session.IsFinalised)
        {
            var processedResult = await queryRepository.GetProcessedArtefactAsync(session, cancellationToken);
            if (processedResult.IsSuccess)
            {
                processedArtifact = processedResult.Value;
            }
        }

        var auditResult = await auditStore.ListAsync(request.SubmissionId, cancellationToken);
        var auditEntries = auditResult is { IsSuccess: true, Value: not null }
            ? auditResult.Value
            : [];

        var resolvedIdentifierType = session.IdentifierType;
        var resolvedIdentifierValue = session.IdentifierValue;
        var isReadyToFinalise = false;
        IReadOnlyList<ValidationError> pendingErrors = [];

        if (!session.IsFinalised)
        {
            if (string.IsNullOrWhiteSpace(mergedDraftPayload))
            {
                pendingErrors = [new ValidationError("$", SubmissionMessages.NoDraftPayloadUploadedYet)];
            }
            else
            {
                var schemaResult = await evaluationService.ResolveSchemaAsync(
                    session.SchemaName,
                    session.SchemaVersion,
                    cancellationToken);

                if (schemaResult.IsSuccess)
                {
                    var evaluationResult = await evaluationService.EvaluateResolvedPayloadAsync(
                        mergedDraftPayload,
                        schemaResult.Value,
                        session,
                        isDraftUpdate: false,
                        cancellationToken);

                    if (evaluationResult.IsSuccess)
                    {
                        var evaluation = evaluationResult.Value;
                        pendingErrors = evaluation.ValidationErrors;
                        isReadyToFinalise = pendingErrors.Count == 0;
                        resolvedIdentifierType ??= evaluation.IdentifierEvaluation.Identifier?.Type;
                        resolvedIdentifierValue ??= evaluation.IdentifierEvaluation.Identifier?.Value;
                    }
                    else
                    {
                        pendingErrors = [new ValidationError("$", evaluationResult.Error.Description ?? evaluationResult.Error.Code)];
                    }
                }
                else
                {
                    pendingErrors = [new ValidationError("$", schemaResult.Error.Description ?? schemaResult.Error.Code)];
                }
            }
        }

        var guidance = SubmissionNextStepsBuilder.Build(
            session.SubmissionId,
            session.IsFinalised,
            session.FinalisedAtUtc,
            hasDraftPayload: !session.IsFinalised && !string.IsNullOrWhiteSpace(mergedDraftPayload),
            draftCount: inventory.Count,
            pendingErrors: pendingErrors);

        return new GetSubmissionDetailResult(
            Session: session,
            RawSessionJson: rawSessionJson,
            DraftItems: inventory.Items,
            MergedDraftPayloadJson: mergedDraftPayload,
            ProcessedArtifact: processedArtifact,
            IsReadyToFinalise: isReadyToFinalise,
            PendingErrors: pendingErrors,
            NextSteps: guidance.NextSteps,
            NextStepsSummary: guidance.Summary,
            ResolvedIdentifierType: resolvedIdentifierType,
            ResolvedIdentifierValue: resolvedIdentifierValue,
            AuditEntries: auditEntries);
    }
}
