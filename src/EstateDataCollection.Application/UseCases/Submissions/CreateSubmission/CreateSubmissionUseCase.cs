using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Contracts;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;

public class CreateSubmissionUseCase(
    ISchemaRegistry registry,
    ISubmissionInfrastructure infrastructure,
    CreateSubmissionUseCaseInstrumentation? instrumentation = null
) : IUseCase<CreateSubmissionResult, CreateSubmissionRequest>
{
    private readonly CreateSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new CreateSubmissionUseCaseInstrumentation(NullLogger<CreateSubmissionUseCase>.Instance);

    public async Task<Result<CreateSubmissionResult>> ExecuteAsync(CreateSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.CreatingSubmissionSession(request.SchemaName, request.Version);

        ResolvedSchema resolvedSchema;

        if (request.Version.HasValue)
        {
            var @explicit =
                await registry.GetExplicitSchemaAsync(request.SchemaName, request.Version.Value, cancellationToken);

            if (@explicit.IsFailure)
            {
                _instrumentation.SchemaResolutionFailed(request.SchemaName, @explicit.Error.Description ?? string.Empty);
                return @explicit.Error;
            }

            if (@explicit.Value.Metadata.IsRetired)
            {
                _instrumentation.SchemaRetired(request.SchemaName, request.Version.Value);
                return Error.Validation(ErrorCodes.SchemaRetired);
            }

            resolvedSchema = @explicit.Value;
        }
        else
        {
            var active =
                await registry.ResolveActiveSchemaAsync(request.SchemaName, DateTimeOffset.UtcNow, cancellationToken);

            if (active.IsFailure)
            {
                _instrumentation.SchemaResolutionFailed(request.SchemaName, active.Error.Description ?? string.Empty);
                return active.Error;
            }
            
            resolvedSchema = active.Value;
        }
        
        var submission = SubmissionSession.Create(
            resolvedSchema.Metadata.Name,
            resolvedSchema.Metadata.Version);

        var outcome = await infrastructure.CreateSessionAsync(submission, cancellationToken);
        
        if (outcome.IsFailure)
        {
            _instrumentation.SubmissionSessionCreationFailed(request.SchemaName, outcome.Error.Description ?? string.Empty);
            return outcome.Error;
        }

        _instrumentation.SubmissionSessionCreated(
            submission.SubmissionId,
            submission.SchemaName,
            submission.SchemaVersion);

        return new CreateSubmissionResult(
            submission.SubmissionId,
            submission.SchemaName,
            submission.SchemaVersion,
            submission.CreatedAtUtc);
    }
}
