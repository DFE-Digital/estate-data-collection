using System.Diagnostics;
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
    ISubmissionAuditStore? auditStore = null
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

        var processes = new List<SubmissionProcessExecution>(7);
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
            var alreadyErr = Error.Forbidden(ErrorCodes.SubmissionAlreadyFinalised);

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.Value.SubmissionId,
                "FinaliseSubmission",
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                "Rejected",
                403,
                processes,
                errorCode: alreadyErr.Code,
                message: alreadyErr.Description,
                identifier: BuildIdentifier(session.Value.IdentifierType, session.Value.IdentifierValue, session.Value.Urn),
                cancellationToken: cancellationToken);

            return alreadyErr;
        }

        var draftSw = Stopwatch.StartNew();
        var draftPayload = await infrastructure.GetDraftPayloadAsync(request.SubmissionId, cancellationToken);
        draftSw.Stop();

        if (draftPayload.IsFailure)
        {
            _instrumentation.DraftPayloadMissing(request.SubmissionId);

            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "BlobStorage:GetDraftPayload",
                Engine = "azure-blob",
                Outcome = "Failed",
                DurationMs = draftSw.ElapsedMilliseconds,
                Summary = draftPayload.Error.Description ?? draftPayload.Error.Code
            });

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.Value.SubmissionId,
                "FinaliseSubmission",
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                "Failed",
                SubmissionAuditRecorder.MapHttpStatus(draftPayload.Error.Type),
                processes,
                errorCode: draftPayload.Error.Code,
                message: draftPayload.Error.Description,
                cancellationToken: cancellationToken);

            return draftPayload.Error;
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "BlobStorage:GetDraftPayload",
            Engine = "azure-blob",
            Outcome = "Succeeded",
            DurationMs = draftSw.ElapsedMilliseconds,
            Summary = $"Loaded {draftPayload.Value.Length} char draft JSON"
        });

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
                "FinaliseSubmission",
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

        IReadOnlyList<ValidationError>? validationErrors;
        if (validationService is not null)
        {
            var combined = await validationService.ValidatePayloadWithTraceAsync(
                draftPayload.Value,
                schema.Value,
                isDraftUpdate: false,
                cancellationToken);

            if (combined.IsFailure)
            {
                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "PayloadValidationService",
                    Engine = "validation-pipeline",
                    Outcome = "Failed",
                    Summary = combined.Error.Description ?? combined.Error.Code
                });

                await SubmissionAuditRecorder.SafeAppendAsync(
                    auditStore,
                    session.Value.SubmissionId,
                    "FinaliseSubmission",
                    session.Value.SchemaName,
                    session.Value.SchemaVersion,
                    "Failed",
                    SubmissionAuditRecorder.MapHttpStatus(combined.Error.Type),
                    processes,
                    errorCode: combined.Error.Code,
                    message: combined.Error.Description,
                    cancellationToken: cancellationToken);

                return combined.Error;
            }

            validationErrors = combined.Value.Errors;
            processes.AddRange(combined.Value.ProcessesExecuted);
        }
        else
        {
            var valSw = Stopwatch.StartNew();
            var validation = await validator.ValidateAsync(draftPayload.Value, schema.Value.Schema, cancellationToken);
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
                    "FinaliseSubmission",
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

            validationErrors = validation.Value.Errors;
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "JsonSchemaValidator",
                Engine = "json-schema",
                Outcome = validationErrors is { Count: > 0 } ? "ValidationErrorsFound" : "Succeeded",
                DurationMs = valSw.ElapsedMilliseconds,
                ItemCount = validationErrors?.Count ?? 0,
                Summary = validationErrors is { Count: > 0 }
                    ? $"{validationErrors.Count} schema error(s)"
                    : "Schema structure valid"
            });
        }

        if (validationErrors is { Count: > 0 })
        {
            _instrumentation.ValidationFailed(request.SubmissionId, validationErrors.Count);
            var contractErr = Error.Validation(ErrorCodes.SchemaContractViolation, errors: validationErrors);

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.Value.SubmissionId,
                "FinaliseSubmission",
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                "Rejected",
                422,
                processes,
                errorCode: contractErr.Code,
                message: $"Finalisation rejected with {validationErrors.Count} validation error(s)",
                validationErrors: validationErrors,
                cancellationToken: cancellationToken);

            return contractErr;
        }

        var idSw = Stopwatch.StartNew();
        var identifierEval = PayloadIdentifierExtractor.Evaluate(draftPayload.Value, schema.Value, _options);
        idSw.Stop();

        if (!identifierEval.IsValid)
        {
            _instrumentation.InvalidUrn(request.SubmissionId, 0);
            var idErr = identifierEval.FailureError ?? Error.Validation(ErrorCodes.PayloadInvalidIdentifier);

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.Value.SubmissionId,
                "FinaliseSubmission",
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                "Rejected",
                SubmissionAuditRecorder.MapHttpStatus(idErr.Type),
                processes,
                errorCode: idErr.Code,
                message: idErr.Description,
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

        var promoSw = Stopwatch.StartNew();
        var promotion = isLegacyCompatibleUrnCall
            ? await infrastructure.PromoteToProcessedAsync(
                session.Value.SubmissionId,
                resolvedUrn,
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                cancellationToken)
            : await infrastructure.PromoteToProcessedAsync(
                session.Value.SubmissionId,
                extractedIdentifier!.Value.Type,
                extractedIdentifier.Value.Value,
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                cancellationToken);
        promoSw.Stop();

        if (promotion.IsFailure)
        {
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "BlobStorage:PromoteToProcessed",
                Engine = "azure-blob",
                Outcome = "Failed",
                DurationMs = promoSw.ElapsedMilliseconds,
                Summary = promotion.Error.Description ?? promotion.Error.Code
            });

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.Value.SubmissionId,
                "FinaliseSubmission",
                session.Value.SchemaName,
                session.Value.SchemaVersion,
                "Failed",
                SubmissionAuditRecorder.MapHttpStatus(promotion.Error.Type),
                processes,
                errorCode: promotion.Error.Code,
                message: promotion.Error.Description,
                identifier: auditIdentifier,
                cancellationToken: cancellationToken);

            _instrumentation.SubmissionPromotionFailed(request.SubmissionId, promotion.Error.Description ?? string.Empty);
            return promotion.Error;
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "BlobStorage:PromoteToProcessed",
            Engine = "azure-blob",
            Outcome = "Succeeded",
            DurationMs = promoSw.ElapsedMilliseconds,
            Summary = "Promoted draft payload to processed container"
        });

        await SubmissionAuditRecorder.SafeAppendAsync(
            auditStore,
            session.Value.SubmissionId,
            "FinaliseSubmission",
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            "Succeeded",
            200,
            processes,
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

    private static SubmissionAuditIdentifier? BuildIdentifier(string? type, string? value, int? urn) =>
        type is null && value is null && urn is null
            ? null
            : new SubmissionAuditIdentifier(type, value, urn);
}
