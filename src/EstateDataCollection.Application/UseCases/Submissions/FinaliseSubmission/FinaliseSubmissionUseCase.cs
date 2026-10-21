using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;

public class FinaliseSubmissionUseCase(
    ISubmissionInfrastructure infrastructure,
    ISchemaRegistry registry,
    ISchemaValidator validator,
    FinaliseSubmissionUseCaseInstrumentation? instrumentation = null
) : IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest>
{
    private readonly FinaliseSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new FinaliseSubmissionUseCaseInstrumentation(NullLogger<FinaliseSubmissionUseCase>.Instance);

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

        var validation = await validator.ValidateAsync(draftPayload.Value, schema.Value.Schema, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.Error;
        }

        if (!validation.Value.IsValid)
        {
            _instrumentation.ValidationFailed(request.SubmissionId, validation.Value.Errors?.Count ?? 0);
            return Error.Validation(ErrorCodes.SchemaContractViolation, errors: validation.Value.Errors);
        }

        var urn = Urn.ExtractFromPayload(draftPayload.Value);

        if (urn.IsFailure)
        {
            _instrumentation.InvalidUrn(request.SubmissionId, 0);
            return urn.Error;
        }
        
        var promotion = await infrastructure.PromoteToProcessedAsync(session.Value.SubmissionId, urn.Value,
            session.Value.SchemaName, session.Value.SchemaVersion, cancellationToken);

        if (promotion.IsFailure)
        {
            _instrumentation.SubmissionPromotionFailed(request.SubmissionId, promotion.Error.Description ?? string.Empty);
            return promotion.Error;
        }

        _instrumentation.SubmissionFinalisedAndPromoted(
            session.Value.SubmissionId,
            urn.Value,
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            promotion.Value);

        return new FinaliseSubmissionResult(
            session.Value.SubmissionId,
            urn.Value,
            session.Value.SchemaName,
            session.Value.SchemaVersion,
            null,
            promotion.Value,
            DateTimeOffset.UtcNow
        );
    }
}
