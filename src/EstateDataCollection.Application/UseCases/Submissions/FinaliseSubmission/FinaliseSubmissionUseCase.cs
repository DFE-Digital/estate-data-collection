using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public class FinaliseSubmissionUseCase(
    ISubmissionInfrastructure infrastructure,
    ISchemaRegistry registry,
    ISchemaValidator validator,
    FinaliseSubmissionUseCaseInstrumentation? instrumentation = null,
    IPayloadValidationService? validationService = null,
    IOptions<ValidationOptions>? validationOptions = null,
    ISubmissionAuditStore? auditStore = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
) : IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest>
{
    private readonly FinaliseSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new FinaliseSubmissionUseCaseInstrumentation(NullLogger<FinaliseSubmissionUseCase>.Instance);

    private readonly ValidationOptions _options =
        validationOptions?.Value ?? new ValidationOptions();

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
                identifier: BuildIdentifier(session.Value.IdentifierType, session.Value.IdentifierValue, session.Value.Urn),
                cancellationToken: cancellationToken);

            return alreadyErr;
        }

        var draftItemsResult = await scope.ExecuteAsync(
            SubmissionProcessCatalog.GetDraftItems,
            async () =>
            {
                var raw = await infrastructure.GetDraftItemsAsync(request.SubmissionId, cancellationToken);
                return raw ?? Result<IReadOnlyList<SubmissionDraftItem>>.Success([]);
            },
            items => new ProcessStepDetails(
                ItemCount: items.Count,
                Summary: items.Count == 0
                    ? "No timestamped draft chunks listed"
                    : $"Loaded {items.Count} draft chunk(s)"),
            err => new ProcessStepDetails(
                SubmissionProcessOutcome.Missing,
                ItemCount: 0,
                Summary: err.Description ?? err.Code));

        var draftItems = draftItemsResult is { IsSuccess: true, Value: not null }
            ? draftItemsResult.Value
            : [];

        Result<string> draftPayload;
        if (draftItems.Count > 1)
        {
            draftPayload = scope.ExecuteResultSync(
                SubmissionProcessCatalog.DraftPayloadMerger,
                () => DraftPayloadMerger.Merge(draftItems),
                _ => new ProcessStepDetails(
                    ItemCount: draftItems.Count,
                    Summary: $"Merged {draftItems.Count} timestamped draft chunk(s) into final candidate"),
                err => new ProcessStepDetails(
                    SubmissionProcessOutcome.Failed,
                    ItemCount: draftItems.Count,
                    Summary: err.Description ?? err.Code));
        }
        else
        {
            draftPayload = await scope.ExecuteAsync(
                SubmissionProcessCatalog.GetDraftPayload,
                () => infrastructure.GetDraftPayloadAsync(request.SubmissionId, cancellationToken),
                payload => new ProcessStepDetails(
                    ItemCount: Math.Max(1, draftItems.Count),
                    Summary: $"Loaded {payload.Length} char draft JSON"));
        }

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

        var schema = await scope.ExecuteAsync(
            SubmissionProcessCatalog.SchemaResolution,
            () => registry.GetExplicitSchemaAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                cancellationToken),
            resolved => new ProcessStepDetails(Summary: $"{resolved.Metadata.Name} v{resolved.Metadata.Version}"));

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

        var validationReport = await SubmissionValidationRunner.ValidateAsync(
            scope,
            validationService,
            validator,
            draftPayload.Value,
            schema.Value,
            isDraftUpdate: false,
            cancellationToken);

        if (validationReport.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                validationReport.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return validationReport.Error;
        }

        var validationErrors = validationReport.Value.Errors;
        if (validationErrors is { Count: > 0 })
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

        var identifierEval = validationReport.Value.IdentifierEvaluation
            ?? scope.ExecuteSync(
                SubmissionProcessCatalog.IdentifierExtractor,
                () => PayloadIdentifierExtractor.Evaluate(draftPayload.Value, schema.Value, _options),
                eval => !eval.IsValid && eval.ValidationError is not null
                    ? new ProcessStepDetails(SubmissionProcessOutcome.ValidationErrorsFound, Summary: eval.ValidationError.Message)
                    : new ProcessStepDetails(
                        Summary: eval.Identifier is { } id ? $"{id.Type}={id.Value}" : "Not required by schema"));

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
        var auditIdentifier = extractedIdentifier is not null
            ? new SubmissionAuditIdentifier(extractedIdentifier.Value.Type, extractedIdentifier.Value.Value, resolvedUrn > 0 ? resolvedUrn : null)
            : null;

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

    private static SubmissionAuditIdentifier? BuildIdentifier(string? type, string? value, int? urn)
    {
        return type is null && value is null && urn is null
            ? null
            : new SubmissionAuditIdentifier(type, value, urn);
    }
}
