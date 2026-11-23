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
    ISubmissionAuditStore? auditStore = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
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
        var actionName = string.IsNullOrWhiteSpace(request.ActionName)
            ? "CheckSubmissionStatus"
            : request.ActionName;

        _instrumentation.CheckingSubmissionStatus(request.SubmissionId);

        using var scope = SubmissionExecutionScope.Start(
            actionName,
            request.SubmissionId,
            processInstrumentation,
            auditStore,
            initialCapacity: 8);

        var sessionResult = await scope.ExecuteAsync(
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
        var draftCount = draftItems.Count;
        DateTimeOffset? lastDraftAtUtc = draftItems.Count > 0
            ? draftItems.Max(d => d.CreatedAtUtc)
            : null;

        if (session.IsFinalised)
        {
            var finalisedGuidance = SubmissionNextStepsBuilder.Build(
                session.SubmissionId,
                isFinalised: true,
                finalisedAtUtc: session.FinalisedAtUtc,
                hasDraftPayload: false,
                draftCount: draftCount,
                pendingErrors: []);

            await scope.RecordAuditAsync(
                session.SchemaName,
                session.SchemaVersion,
                "Finalised",
                200,
                message: finalisedGuidance.Summary,
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
                IdentifierType: session.IdentifierType,
                DraftCount: draftCount,
                LastDraftAtUtc: lastDraftAtUtc,
                NextSteps: finalisedGuidance.NextSteps,
                Summary: finalisedGuidance.Summary);
        }

        Result<string> draftPayload;
        if (draftCount > 1)
        {
            draftPayload = scope.ExecuteResultSync(
                SubmissionProcessCatalog.DraftPayloadMerger,
                () => DraftPayloadMerger.Merge(draftItems),
                _ => new ProcessStepDetails(
                    ItemCount: draftCount,
                    Summary: $"Merged {draftCount} timestamped draft submission(s)"),
                err => new ProcessStepDetails(
                    SubmissionProcessOutcome.Failed,
                    ItemCount: draftCount,
                    Summary: err.Description ?? err.Code));
        }
        else
        {
            draftPayload = await scope.ExecuteAsync(
                SubmissionProcessCatalog.GetDraftPayload,
                () => infrastructure.GetDraftPayloadAsync(request.SubmissionId, cancellationToken),
                payload => new ProcessStepDetails(
                    ItemCount: Math.Max(1, draftCount),
                    Summary: $"Loaded {payload.Length} char draft JSON"),
                err => IsMissingDraftError(err)
                    ? new ProcessStepDetails(SubmissionProcessOutcome.Missing, Summary: "No draft payload uploaded yet")
                    : new ProcessStepDetails(SubmissionProcessOutcome.Failed, Summary: err.Description ?? err.Code));
        }

        if (draftPayload.IsFailure)
        {
            if (IsMissingDraftError(draftPayload.Error))
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

                await scope.RecordAuditAsync(
                    session.SchemaName,
                    session.SchemaVersion,
                    "DraftAwaitingPayload",
                    200,
                    message: emptyGuidance.Summary,
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
                    IdentifierType: session.IdentifierType,
                    DraftCount: 0,
                    LastDraftAtUtc: null,
                    NextSteps: emptyGuidance.NextSteps,
                    Summary: emptyGuidance.Summary);
            }

            await scope.RecordErrorAuditAsync(
                session.SchemaName,
                session.SchemaVersion,
                draftPayload.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return draftPayload.Error;
        }

        var effectiveDraftCount = Math.Max(1, draftCount);

        var schemaResult = await scope.ExecuteAsync(
            SubmissionProcessCatalog.SchemaResolution,
            () => registry.GetExplicitSchemaAsync(
                session.SchemaName,
                session.SchemaVersion,
                cancellationToken),
            resolved => new ProcessStepDetails(Summary: $"{resolved.Metadata.Name} v{resolved.Metadata.Version}"));

        if (schemaResult.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.SchemaName,
                session.SchemaVersion,
                schemaResult.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return schemaResult.Error;
        }

        var validationReport = await SubmissionValidationRunner.ValidateAsync(
            scope,
            validationService,
            validator,
            draftPayload.Value,
            schemaResult.Value,
            isDraftUpdate: false,
            cancellationToken);

        if (validationReport.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                session.SchemaName,
                session.SchemaVersion,
                validationReport.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            return validationReport.Error;
        }

        var pendingErrors = validationReport.Value.Errors.ToList();
        var identifierEval = validationReport.Value.IdentifierEvaluation
            ?? scope.ExecuteSync(
                SubmissionProcessCatalog.IdentifierExtractor,
                () => PayloadIdentifierExtractor.Evaluate(draftPayload.Value, schemaResult.Value, _options),
                eval => !eval.IsValid && eval.ValidationError is not null
                    ? new ProcessStepDetails(SubmissionProcessOutcome.ValidationErrorsFound, Summary: eval.ValidationError.Message)
                    : new ProcessStepDetails(
                        Summary: eval.Identifier is { } id ? $"{id.Type}={id.Value}" : "Not required by schema"));

        if (!identifierEval.IsValid && identifierEval.ValidationError is not null &&
            !pendingErrors.Any(e => string.Equals(e.PropertyPath, identifierEval.ValidationError.PropertyPath, StringComparison.OrdinalIgnoreCase)))
        {
            pendingErrors.Add(identifierEval.ValidationError);
        }

        var isReadyToFinalise = pendingErrors.Count == 0;
        var resolvedIdentifier = identifierEval.Identifier?.Value ?? session.IdentifierValue;
        var resolvedIdentifierType = identifierEval.Identifier?.Type ?? session.IdentifierType;
        var resolvedUrn = identifierEval.Identifier?.AsPositiveInt ?? session.Urn;

        var draftGuidance = SubmissionNextStepsBuilder.Build(
            session.SubmissionId,
            isFinalised: false,
            finalisedAtUtc: null,
            hasDraftPayload: true,
            draftCount: effectiveDraftCount,
            pendingErrors: pendingErrors);

        await scope.RecordAuditAsync(
            session.SchemaName,
            session.SchemaVersion,
            isReadyToFinalise ? "ReadyToFinalise" : "DraftPendingErrors",
            200,
            message: draftGuidance.Summary,
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
            IdentifierType: resolvedIdentifierType,
            DraftCount: effectiveDraftCount,
            LastDraftAtUtc: lastDraftAtUtc,
            NextSteps: draftGuidance.NextSteps,
            Summary: draftGuidance.Summary);
    }

    private static bool IsMissingDraftError(Error error)
    {
        return string.Equals(error.Code, ErrorCodes.SubmissionNotFound.GetName(), StringComparison.Ordinal) ||
               string.Equals(error.Code, ErrorCodes.PayloadNotFound.GetName(), StringComparison.Ordinal) ||
               string.Equals(error.Code, ErrorCodes.PayloadEmpty.GetName(), StringComparison.Ordinal);
    }
}
