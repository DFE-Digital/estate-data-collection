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

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;

public class GetSubmissionStatusUseCase(
    ISubmissionInfrastructure infrastructure,
    ISchemaRegistry registry,
    ISchemaValidator validator,
    GetSubmissionStatusUseCaseInstrumentation? instrumentation = null,
    IPayloadValidationService? validationService = null,
    IOptions<ValidationOptions>? validationOptions = null,
    ISubmissionAuditStore? auditStore = null
) : IUseCase<GetSubmissionStatusResult, GetSubmissionStatusRequest>
{
    private readonly GetSubmissionStatusUseCaseInstrumentation _instrumentation =
        instrumentation ?? new GetSubmissionStatusUseCaseInstrumentation(NullLogger<GetSubmissionStatusUseCase>.Instance);

    private readonly ValidationOptions _options =
        validationOptions?.Value ?? new ValidationOptions();

    public async Task<Result<GetSubmissionStatusResult>> ExecuteAsync(
        GetSubmissionStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.CheckingSubmissionStatus(request.SubmissionId);

        var processes = new List<SubmissionProcessExecution>(6);
        var sessionSw = Stopwatch.StartNew();
        var sessionResult = await infrastructure.GetSessionAsync(request.SubmissionId, cancellationToken);
        sessionSw.Stop();

        if (sessionResult.IsFailure)
        {
            _instrumentation.SubmissionNotFound(request.SubmissionId);
            return sessionResult.Error;
        }

        var session = sessionResult.Value;
        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "BlobStorage:GetSession",
            Engine = "azure-blob",
            Outcome = "Succeeded",
            DurationMs = sessionSw.ElapsedMilliseconds,
            Summary = session.IsFinalised ? "Session finalised" : "Draft session loaded"
        });

        if (session.IsFinalised)
        {
            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.SubmissionId,
                "CheckSubmissionStatus",
                session.SchemaName,
                session.SchemaVersion,
                "Finalised",
                200,
                processes,
                message: "Submission is already finalised",
                identifier: new SubmissionAuditIdentifier(session.IdentifierType, session.IdentifierValue, session.Urn),
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
                IdentifierType: session.IdentifierType);
        }

        var draftSw = Stopwatch.StartNew();
        var draftPayload = await infrastructure.GetDraftPayloadAsync(request.SubmissionId, cancellationToken);
        draftSw.Stop();

        if (draftPayload.IsFailure)
        {
            if (IsMissingDraftError(draftPayload.Error))
            {
                IReadOnlyList<ValidationError> missingDraftErrors =
                [
                    new ValidationError("$", "No draft payload has been uploaded yet.")
                ];

                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "BlobStorage:GetDraftPayload",
                    Engine = "azure-blob",
                    Outcome = "Missing",
                    DurationMs = draftSw.ElapsedMilliseconds,
                    Summary = "No draft.json uploaded yet"
                });

                await SubmissionAuditRecorder.SafeAppendAsync(
                    auditStore,
                    session.SubmissionId,
                    "CheckSubmissionStatus",
                    session.SchemaName,
                    session.SchemaVersion,
                    "DraftAwaitingPayload",
                    200,
                    processes,
                    message: "No draft payload uploaded yet",
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
                    IdentifierType: session.IdentifierType);
            }

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
                session.SubmissionId,
                "CheckSubmissionStatus",
                session.SchemaName,
                session.SchemaVersion,
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
        var schemaResult = await registry.GetExplicitSchemaAsync(
            session.SchemaName,
            session.SchemaVersion,
            cancellationToken);
        schemaSw.Stop();

        if (schemaResult.IsFailure)
        {
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "SchemaResolution",
                Engine = "schema-registry",
                Outcome = "Failed",
                DurationMs = schemaSw.ElapsedMilliseconds,
                Summary = schemaResult.Error.Description ?? schemaResult.Error.Code
            });

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                session.SubmissionId,
                "CheckSubmissionStatus",
                session.SchemaName,
                session.SchemaVersion,
                "Failed",
                SubmissionAuditRecorder.MapHttpStatus(schemaResult.Error.Type),
                processes,
                errorCode: schemaResult.Error.Code,
                message: schemaResult.Error.Description,
                cancellationToken: cancellationToken);

            return schemaResult.Error;
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "SchemaResolution",
            Engine = "schema-registry",
            Outcome = "Succeeded",
            DurationMs = schemaSw.ElapsedMilliseconds,
            Summary = $"{schemaResult.Value.Metadata.Name} v{schemaResult.Value.Metadata.Version}"
        });

        IReadOnlyList<ValidationError> validationErrors;
        if (validationService is not null)
        {
            var combined = await validationService.ValidatePayloadWithTraceAsync(
                draftPayload.Value,
                schemaResult.Value,
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
                    session.SubmissionId,
                    "CheckSubmissionStatus",
                    session.SchemaName,
                    session.SchemaVersion,
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
            var validation = await validator.ValidateAsync(
                draftPayload.Value,
                schemaResult.Value.Schema,
                cancellationToken);
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
                    session.SubmissionId,
                    "CheckSubmissionStatus",
                    session.SchemaName,
                    session.SchemaVersion,
                    "Failed",
                    SubmissionAuditRecorder.MapHttpStatus(validation.Error.Type),
                    processes,
                    errorCode: validation.Error.Code,
                    message: validation.Error.Description,
                    cancellationToken: cancellationToken);

                return validation.Error;
            }

            validationErrors = validation.Value.Errors ?? [];
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "JsonSchemaValidator",
                Engine = "json-schema",
                Outcome = validationErrors.Count == 0 ? "Succeeded" : "ValidationErrorsFound",
                DurationMs = valSw.ElapsedMilliseconds,
                ItemCount = validationErrors.Count,
                Summary = validationErrors.Count == 0 ? "Schema structure valid" : $"{validationErrors.Count} schema error(s)"
            });
        }

        var pendingErrors = validationErrors.ToList();
        var identifierEval = PayloadIdentifierExtractor.Evaluate(draftPayload.Value, schemaResult.Value, _options);
        if (!identifierEval.IsValid && identifierEval.ValidationError is not null &&
            !pendingErrors.Any(e => string.Equals(e.PropertyPath, identifierEval.ValidationError.PropertyPath, StringComparison.OrdinalIgnoreCase)))
        {
            pendingErrors.Add(identifierEval.ValidationError);
        }

        var isReadyToFinalise = pendingErrors.Count == 0;
        var resolvedIdentifier = identifierEval.Identifier?.Value ?? session.IdentifierValue;
        var resolvedIdentifierType = identifierEval.Identifier?.Type ?? session.IdentifierType;
        var resolvedUrn = identifierEval.Identifier?.AsPositiveInt ?? session.Urn;

        await SubmissionAuditRecorder.SafeAppendAsync(
            auditStore,
            session.SubmissionId,
            "CheckSubmissionStatus",
            session.SchemaName,
            session.SchemaVersion,
            isReadyToFinalise ? "ReadyToFinalise" : "DraftPendingErrors",
            200,
            processes,
            message: isReadyToFinalise
                ? "Draft passes all checks and is ready to finalise"
                : $"Draft has {pendingErrors.Count} pending error(s)",
            validationErrors: pendingErrors,
            identifier: resolvedIdentifierType is not null || resolvedIdentifier is not null || resolvedUrn is not null
                ? new SubmissionAuditIdentifier(resolvedIdentifierType, resolvedIdentifier, resolvedUrn)
                : null,
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
            IdentifierType: resolvedIdentifierType);
    }

    private static bool IsMissingDraftError(Error error) =>
        string.Equals(error.Code, ErrorCodes.SubmissionNotFound.GetName(), StringComparison.Ordinal) ||
        string.Equals(error.Code, ErrorCodes.PayloadNotFound.GetName(), StringComparison.Ordinal) ||
        string.Equals(error.Code, ErrorCodes.PayloadEmpty.GetName(), StringComparison.Ordinal);
}
