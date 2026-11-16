using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;

public class UpdateSubmissionUseCase(
    ISubmissionInfrastructure infrastructure,
    ISchemaRegistry registry,
    ISchemaValidator validator,
    UpdateSubmissionUseCaseInstrumentation? instrumentation = null,
    IPayloadValidationService? validationService = null
) : IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest>
{
    private readonly UpdateSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new UpdateSubmissionUseCaseInstrumentation(NullLogger<UpdateSubmissionUseCase>.Instance);

    public async Task<Result<UpdateSubmissionResult>> ExecuteAsync(UpdateSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        _instrumentation.UpdatingDraftPayload(request.SubmissionId);

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

        var schema = await registry.GetExplicitSchemaAsync(session.Value.SchemaName, session.Value.SchemaVersion,
            cancellationToken);
        
        if (schema.IsFailure)
        {
            return schema.Error;
        }

        IReadOnlyList<ValidationError> errors;
        if (validationService is not null)
        {
            var combinedResult = await validationService.ValidatePayloadAsync(
                request.Payload,
                schema.Value,
                isDraftUpdate: true,
                cancellationToken);

            if (combinedResult.IsFailure)
            {
                return combinedResult.Error;
            }

            errors = combinedResult.Value;
        }
        else
        {
            var validation = await validator.ValidateAsync(request.Payload, schema.Value.Schema, cancellationToken);

            if (validation.IsFailure)
            {
                return validation.Error;
            }

            errors = validation.Value.Errors ?? [];
        }
        
        var save = await infrastructure.SaveDraftPayloadAsync(request.SubmissionId, null, request.Payload,
            cancellationToken);

        if (save.IsFailure)
        {
            _instrumentation.DraftPayloadSaveFailed(request.SubmissionId, save.Error.Description ?? string.Empty);
            return save.Error;
        }
        
        var isValid = errors.Count == 0;

        if (!isValid)
        {
            _instrumentation.DraftValidationFailed(request.SubmissionId, errors.Count);
        }

        _instrumentation.DraftPayloadSaved(request.SubmissionId, isValid, errors.Count);

        return new UpdateSubmissionResult(
            SubmissionId: request.SubmissionId,
            IsSuccessful: isValid,
            PendingErrors: errors,
            SavedAtUtc: DateTimeOffset.UtcNow
        );
    }
}
