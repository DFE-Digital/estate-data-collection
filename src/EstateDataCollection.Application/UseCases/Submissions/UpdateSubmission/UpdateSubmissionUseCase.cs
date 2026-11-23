using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public class UpdateSubmissionUseCase(
    ISubmissionInfrastructure infrastructure,
    ISchemaRegistry registry,
    ISchemaValidator validator,
    UpdateSubmissionUseCaseInstrumentation? instrumentation = null,
    IPayloadValidationService? validationService = null,
    ISubmissionAuditStore? auditStore = null,
    IOptions<ValidationOptions>? validationOptions = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
) : IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest>
{
    private readonly UpdateSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new UpdateSubmissionUseCaseInstrumentation(NullLogger<UpdateSubmissionUseCase>.Instance);

    private readonly ValidationOptions _options =
        validationOptions?.Value ?? new ValidationOptions();

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
                identifier: BuildIdentifier(session.Value.IdentifierType, session.Value.IdentifierValue, session.Value.Urn),
                cancellationToken: cancellationToken);

            return finalisedErr;
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
                    ? "No prior draft chunks"
                    : $"Loaded {items.Count} prior draft chunk(s)"),
            err => new ProcessStepDetails(
                SubmissionProcessOutcome.Missing,
                ItemCount: 0,
                Summary: err.Description ?? err.Code));

        var existingItems = draftItemsResult is { IsSuccess: true, Value: not null }
            ? draftItemsResult.Value
            : [];

        var draftCountAfterSave = existingItems.Count + 1;
        string payloadToValidate;

        if (existingItems.Count > 0)
        {
            var payloadsToMerge = existingItems.Select(i => i.PayloadJson).Append(request.Payload).ToList();
            var mergeResult = scope.ExecuteResultSync(
                SubmissionProcessCatalog.DraftPayloadMerger,
                () => DraftPayloadMerger.MergeJsonPayloads(payloadsToMerge),
                _ => new ProcessStepDetails(
                    ItemCount: payloadsToMerge.Count,
                    Summary: $"Merged incoming patch with {existingItems.Count} prior draft chunk(s)"),
                err => new ProcessStepDetails(
                    SubmissionProcessOutcome.Failed,
                    ItemCount: payloadsToMerge.Count,
                    Summary: err.Description ?? err.Code));

            if (mergeResult.IsFailure)
            {
                await scope.RecordErrorAuditAsync(
                    session.Value.SchemaName,
                    session.Value.SchemaVersion,
                    mergeResult.Error,
                    outcomeStatus: "Rejected",
                    cancellationToken: cancellationToken);

                return mergeResult.Error;
            }

            payloadToValidate = mergeResult.Value;
        }
        else
        {
            payloadToValidate = request.Payload;
        }

        var validationReport = await SubmissionValidationRunner.ValidateAsync(
            scope,
            validationService,
            validator,
            payloadToValidate,
            schema.Value,
            isDraftUpdate: true,
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

        var errors = validationReport.Value.Errors;
        var idEval = validationReport.Value.IdentifierEvaluation
            ?? PayloadIdentifierExtractor.Evaluate(payloadToValidate, schema.Value, _options);
        var auditIdentifier = idEval.Identifier is { } extracted
            ? new SubmissionAuditIdentifier(extracted.Type, extracted.Value, extracted.AsPositiveInt)
            : BuildIdentifier(session.Value.IdentifierType, session.Value.IdentifierValue, session.Value.Urn);

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

    private static SubmissionAuditIdentifier? BuildIdentifier(string? type, string? value, int? urn)
    {
        return type is null && value is null && urn is null
            ? null
            : new SubmissionAuditIdentifier(type, value, urn);
    }
}
