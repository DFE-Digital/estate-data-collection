using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Common.Results;
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
    IOptions<ValidationOptions>? validationOptions = null
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

        var session = await infrastructure.GetSessionAsync(request.SubmissionId, cancellationToken);

        if (session.IsFailure)
        {
            _instrumentation.SubmissionNotFound(request.SubmissionId);
            return session.Error;
        }

        if (session.Value.IsFinalised)
        {
            _instrumentation.SubmissionAlreadyFinalised(request.SubmissionId);
            return Error.Forbidden(ErrorCodes.SubmissionAlreadyFinalised);
        }

        var draftPayload = await infrastructure.GetDraftPayloadAsync(request.SubmissionId, cancellationToken);

        if (draftPayload.IsFailure)
        {
            _instrumentation.DraftPayloadMissing(request.SubmissionId);
            return draftPayload.Error;
        }

        var schema = await registry.GetExplicitSchemaAsync(session.Value.SchemaName, session.Value.SchemaVersion,
            cancellationToken);

        if (schema.IsFailure)
        {
            return schema.Error;
        }

        IReadOnlyList<ValidationError>? validationErrors;
        if (validationService is not null)
        {
            var combined = await validationService.ValidatePayloadAsync(
                draftPayload.Value,
                schema.Value,
                isDraftUpdate: false,
                cancellationToken);

            if (combined.IsFailure)
            {
                return combined.Error;
            }

            validationErrors = combined.Value;
        }
        else
        {
            var validation = await validator.ValidateAsync(draftPayload.Value, schema.Value.Schema, cancellationToken);

            if (validation.IsFailure)
            {
                return validation.Error;
            }

            validationErrors = validation.Value.Errors;
        }

        if (validationErrors is { Count: > 0 })
        {
            _instrumentation.ValidationFailed(request.SubmissionId, validationErrors.Count);
            return Error.Validation(ErrorCodes.SchemaContractViolation, errors: validationErrors);
        }

        var identifierConfig = schema.Value.Metadata.IdentifierConfig;
        var isIdentifierDisabled = string.Equals(identifierConfig?.Type, "None", StringComparison.OrdinalIgnoreCase);

        int resolvedUrn;
        if (isIdentifierDisabled)
        {
            resolvedUrn = 0;
        }
        else
        {
            var searchPaths = !string.IsNullOrWhiteSpace(identifierConfig?.JsonPointer)
                ? [identifierConfig.JsonPointer]
                : _options.UrnJsonPaths;

            var requireUrn = identifierConfig?.Required ?? _options.RequireUrnOnFinalise;
            var urn = Urn.ExtractFromPayload(draftPayload.Value, searchPaths);

            if (urn.IsFailure)
            {
                if (requireUrn)
                {
                    _instrumentation.InvalidUrn(request.SubmissionId, 0);
                    return urn.Error;
                }
                resolvedUrn = 0;
            }
            else
            {
                resolvedUrn = urn.Value;
            }
        }
        
        var promotion = await infrastructure.PromoteToProcessedAsync(session.Value.SubmissionId, resolvedUrn,
            session.Value.SchemaName, session.Value.SchemaVersion, cancellationToken);

        if (promotion.IsFailure)
        {
            _instrumentation.SubmissionPromotionFailed(request.SubmissionId, promotion.Error.Description ?? string.Empty);
            return promotion.Error;
        }

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
            DateTimeOffset.UtcNow
        );
    }
}
