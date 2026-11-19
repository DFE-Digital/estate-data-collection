using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Extensions;
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
    IOptions<ValidationOptions>? validationOptions = null
) : IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest>
{
    private readonly UpdateSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new UpdateSubmissionUseCaseInstrumentation(NullLogger<UpdateSubmissionUseCase>.Instance);

    private readonly ValidationOptions _options =
        validationOptions?.Value ?? new ValidationOptions();

    public async Task<Result<UpdateSubmissionResult>> ExecuteAsync(UpdateSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        _instrumentation.UpdatingDraftPayload(request.SubmissionId);

        var processes = new List<SubmissionProcessExecution>(6);
        var sessionSw = Stopwatch.StartNew();
        var session = await infrastructure.GetSessionAsync(request.SubmissionId, cancellationToken);
        sessionSw.Stop();

        if (session.IsFailure)
        {
            _instrumentation.SubmissionNotFound(request.SubmissionId);
            return session.Error;
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "BlobStorage:GetSession",
            Engine = "azure-blob",
            Outcome = "Succeeded",
            DurationMs = sessionSw.ElapsedMilliseconds,
            Summary = session.Value.IsFinalised ? "Session already finalised" : "Draft session loaded"
        });

        if (session.Value.IsFinalised)
        {
            _instrumentation.SubmissionAlreadyFinalised(request.SubmissionId);

            var finalisedErr = Error.Forbidden(ErrorCodes.SubmissionAlreadyFinalised);
            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.Value.SubmissionId,
                "UpdateSubmission",
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                "Rejected",
                403,
                processes,
                errorCode: finalisedErr.Code,
                message: finalisedErr.Description,
                identifier: BuildIdentifier(session.Value.IdentifierType, session.Value.IdentifierValue, session.Value.Urn),
                cancellationToken: cancellationToken);

            return finalisedErr;
        }

        var schemaSw = Stopwatch.StartNew();
        var schema = await registry.GetExplicitSchemaAsync(
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            cancellationToken);
        schemaSw.Stop();

        if (schema.IsFailure)
        {
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "SchemaResolution",
                Engine = "schema-registry",
                Outcome = "Failed",
                DurationMs = schemaSw.ElapsedMilliseconds,
                Summary = schema.Error.Description ?? schema.Error.Code
            });

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.Value.SubmissionId,
                "UpdateSubmission",
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                "Failed",
                SubmissionAuditRecorder.MapHttpStatus(schema.Error.Type),
                processes,
                errorCode: schema.Error.Code,
                message: schema.Error.Description,
                cancellationToken: cancellationToken);

            return schema.Error;
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "SchemaResolution",
            Engine = "schema-registry",
            Outcome = "Succeeded",
            DurationMs = schemaSw.ElapsedMilliseconds,
            Summary = $"{schema.Value.Metadata.Name} v{schema.Value.Metadata.Version}"
        });

        IReadOnlyList<ValidationError> errors;
        if (validationService is not null)
        {
            var combinedResult = await validationService.ValidatePayloadWithTraceAsync(
                request.Payload,
                schema.Value,
                isDraftUpdate: true,
                cancellationToken);

            if (combinedResult.IsFailure)
            {
                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "PayloadValidationService",
                    Engine = "validation-pipeline",
                    Outcome = "Failed",
                    Summary = combinedResult.Error.Description ?? combinedResult.Error.Code
                });

                await SubmissionAuditRecorder.SafeAppendAsync(
                    auditStore,
                    session.Value.SubmissionId,
                    "UpdateSubmission",
                    session.Value.SchemaName,
                    session.Value.SchemaVersion,
                    "Failed",
                    SubmissionAuditRecorder.MapHttpStatus(combinedResult.Error.Type),
                    processes,
                    errorCode: combinedResult.Error.Code,
                    message: combinedResult.Error.Description,
                    cancellationToken: cancellationToken);

                return combinedResult.Error;
            }

            errors = combinedResult.Value.Errors;
            processes.AddRange(combinedResult.Value.ProcessesExecuted);
        }
        else
        {
            var valSw = Stopwatch.StartNew();
            var validation = await validator.ValidateAsync(request.Payload, schema.Value.Schema, cancellationToken);
            valSw.Stop();

            if (validation.IsFailure)
            {
                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "JsonSchemaValidator",
                    Engine = "json-schema",
                    Outcome = "Failed",
                    DurationMs = valSw.ElapsedMilliseconds,
                    Summary = validation.Error.Description ?? validation.Error.Code
                });

                await SubmissionAuditRecorder.SafeAppendAsync(
                    auditStore,
                    session.Value.SubmissionId,
                    "UpdateSubmission",
                    session.Value.SchemaName,
                    session.Value.SchemaVersion,
                    "Failed",
                    SubmissionAuditRecorder.MapHttpStatus(validation.Error.Type),
                    processes,
                    errorCode: validation.Error.Code,
                    message: validation.Error.Description,
                    cancellationToken: cancellationToken);

                return validation.Error;
            }

            errors = validation.Value.Errors ?? [];
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "JsonSchemaValidator",
                Engine = "json-schema",
                Outcome = errors.Count == 0 ? "Succeeded" : "ValidationErrorsFound",
                DurationMs = valSw.ElapsedMilliseconds,
                ItemCount = errors.Count,
                Summary = errors.Count == 0 ? "Schema structure valid" : $"{errors.Count} schema error(s)"
            });
        }

        var idEval = PayloadIdentifierExtractor.Evaluate(request.Payload, schema.Value, _options);
        var auditIdentifier = idEval.Identifier is { } extracted
            ? new SubmissionAuditIdentifier(extracted.Type, extracted.Value, extracted.AsPositiveInt)
            : BuildIdentifier(session.Value.IdentifierType, session.Value.IdentifierValue, session.Value.Urn);

        var saveSw = Stopwatch.StartNew();
        var save = await infrastructure.SaveDraftPayloadAsync(request.SubmissionId, null, request.Payload,
            cancellationToken);
        saveSw.Stop();

        if (save.IsFailure)
        {
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "BlobStorage:SaveDraftPayload",
                Engine = "azure-blob",
                Outcome = "Failed",
                DurationMs = saveSw.ElapsedMilliseconds,
                Summary = save.Error.Description ?? save.Error.Code
            });

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.Value.SubmissionId,
                "UpdateSubmission",
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                "Failed",
                SubmissionAuditRecorder.MapHttpStatus(save.Error.Type),
                processes,
                errorCode: save.Error.Code,
                message: save.Error.Description,
                validationErrors: errors,
                identifier: auditIdentifier,
                cancellationToken: cancellationToken);

            _instrumentation.DraftPayloadSaveFailed(request.SubmissionId, save.Error.Description ?? string.Empty);
            return save.Error;
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "BlobStorage:SaveDraftPayload",
            Engine = "azure-blob",
            Outcome = "Succeeded",
            DurationMs = saveSw.ElapsedMilliseconds,
            Summary = $"Saved {request.Payload.Length} char draft JSON"
        });

        var isValid = errors.Count == 0;

        if (!isValid)
        {
            _instrumentation.DraftValidationFailed(request.SubmissionId, errors.Count);
        }

        await SubmissionAuditRecorder.SafeAppendAsync(
            auditStore,
            session.Value.SubmissionId,
            "UpdateSubmission",
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            isValid ? "Succeeded" : "SavedWithPendingErrors",
            200,
            processes,
            message: isValid
                ? "Draft payload saved with no validation errors"
                : $"Draft payload saved with {errors.Count} pending validation error(s)",
            validationErrors: errors,
            identifier: auditIdentifier,
            cancellationToken: cancellationToken);

        _instrumentation.DraftPayloadSaved(request.SubmissionId, isValid, errors.Count);

        return new UpdateSubmissionResult(
            SubmissionId: request.SubmissionId,
            IsSuccessful: isValid,
            PendingErrors: errors,
            SavedAtUtc: DateTimeOffset.UtcNow
        );
    }

    private static SubmissionAuditIdentifier? BuildIdentifier(string? type, string? value, int? urn) =>
        type is null && value is null && urn is null
            ? null
            : new SubmissionAuditIdentifier(type, value, urn);
}
