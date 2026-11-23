using System.Text.Json;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;

public sealed class GetSubmissionDetailUseCase(
    ISubmissionInfrastructure submissionInfrastructure,
    ISubmissionQueryRepository queryRepository,
    ISubmissionAuditStore auditStore,
    ISchemaRegistry schemaRegistry,
    ISchemaValidator schemaValidator,
    IPayloadValidationService? validationService = null,
    IOptions<ValidationOptions>? validationOptions = null
) : IUseCase<GetSubmissionDetailResult, GetSubmissionDetailRequest>
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ValidationOptions _options =
        validationOptions?.Value ?? new ValidationOptions();

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

        var draftItemsResult = await submissionInfrastructure.GetDraftItemsAsync(request.SubmissionId, cancellationToken);
        var draftItems = draftItemsResult is { IsSuccess: true, Value: not null }
            ? draftItemsResult.Value
            : [];

        string? mergedDraftPayload = null;
        if (draftItems.Count > 0)
        {
            var merged = DraftPayloadMerger.Merge(draftItems);
            mergedDraftPayload = merged.IsSuccess ? merged.Value : draftItems[^1].PayloadJson;
        }
        else if (!session.IsFinalised)
        {
            var activeDraft = await submissionInfrastructure.GetDraftPayloadAsync(request.SubmissionId, cancellationToken);
            if (activeDraft.IsSuccess)
            {
                mergedDraftPayload = activeDraft.Value;
            }
        }

        SubmissionProcessedArtifact? processedArtifact = null;
        if (session.IsFinalised)
        {
            var processedResult = await queryRepository.GetProcessedArtifactAsync(session, cancellationToken);
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
        var resolvedUrn = session.Urn;
        var isReadyToFinalise = false;
        IReadOnlyList<ValidationError> pendingErrors = [];

        if (!session.IsFinalised)
        {
            if (string.IsNullOrWhiteSpace(mergedDraftPayload))
            {
                pendingErrors = [new ValidationError("$", "No draft payload has been uploaded yet.")];
            }
            else
            {
                var schemaResult = await schemaRegistry.GetExplicitSchemaAsync(
                    session.SchemaName,
                    session.SchemaVersion,
                    cancellationToken);

                if (schemaResult.IsSuccess)
                {
                    var errors = new List<ValidationError>();
                    PayloadIdentifierExtractor.ExtractionEvaluation? idEval = null;

                    if (validationService is not null)
                    {
                        var combined = await validationService.ValidatePayloadWithTraceAsync(
                            mergedDraftPayload,
                            schemaResult.Value,
                            isDraftUpdate: false,
                            cancellationToken);

                        if (combined.IsSuccess)
                        {
                            errors.AddRange(combined.Value.Errors);
                            idEval = combined.Value.IdentifierEvaluation;
                        }
                        else
                        {
                            errors.Add(new ValidationError("$", combined.Error.Description ?? combined.Error.Code));
                        }
                    }
                    else
                    {
                        var validation = await schemaValidator.ValidateAsync(
                            mergedDraftPayload,
                            schemaResult.Value.Schema,
                            cancellationToken);
                        if (validation.IsSuccess && validation.Value.Errors is not null)
                        {
                            errors.AddRange(validation.Value.Errors);
                        }
                    }

                    idEval ??= PayloadIdentifierExtractor.Evaluate(mergedDraftPayload, schemaResult.Value, _options);
                    if (!idEval.IsValid && idEval.ValidationError is not null &&
                        !errors.Any(e => string.Equals(e.PropertyPath, idEval.ValidationError.PropertyPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add(idEval.ValidationError);
                    }

                    resolvedIdentifierType ??= idEval.Identifier?.Type;
                    resolvedIdentifierValue ??= idEval.Identifier?.Value;
                    resolvedUrn ??= idEval.Identifier?.AsPositiveInt;
                    pendingErrors = errors;
                    isReadyToFinalise = errors.Count == 0;
                }
            }
        }

        var guidance = SubmissionNextStepsBuilder.Build(
            session.SubmissionId,
            session.IsFinalised,
            session.FinalisedAtUtc,
            hasDraftPayload: !session.IsFinalised && !string.IsNullOrWhiteSpace(mergedDraftPayload),
            draftCount: draftItems.Count,
            pendingErrors: pendingErrors);

        return new GetSubmissionDetailResult(
            Session: session,
            RawSessionJson: rawSessionJson,
            DraftItems: draftItems,
            MergedDraftPayloadJson: mergedDraftPayload,
            ProcessedArtifact: processedArtifact,
            IsReadyToFinalise: isReadyToFinalise,
            PendingErrors: pendingErrors,
            NextSteps: guidance.NextSteps,
            NextStepsSummary: guidance.Summary,
            ResolvedIdentifierType: resolvedIdentifierType,
            ResolvedIdentifierValue: resolvedIdentifierValue,
            ResolvedUrn: resolvedUrn,
            AuditEntries: auditEntries);
    }
}
