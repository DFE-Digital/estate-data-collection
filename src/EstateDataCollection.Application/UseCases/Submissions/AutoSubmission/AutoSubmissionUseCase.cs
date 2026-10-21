using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Contracts;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.AutoSubmission;

[Obsolete]
public class AutoSubmissionUseCase(
    ISchemaRegistry registry,
    ISchemaValidator validator,
    ISubmissionInfrastructure submissionInfrastructure,
    AutoSubmissionUseCaseInstrumentation? instrumentation = null
) : IUseCase<AutoSubmissionResult, AutoSubmissionRequest>
{
    private readonly AutoSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new AutoSubmissionUseCaseInstrumentation(NullLogger<AutoSubmissionUseCase>.Instance);

    public async Task<Result<AutoSubmissionResult>> ExecuteAsync(AutoSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        _instrumentation.ProcessingAutoSubmission(request.SchemaName, request.Version);

        var submissionTime = DateTimeOffset.UtcNow;
        ResolvedSchema? resolvedSchema;

        if (request.Version.HasValue)
        {
            var @explicit =
                await registry.GetExplicitSchemaAsync(request.SchemaName, request.Version.Value,
                    cancellationToken);

            if (@explicit.IsFailure)
            {
                return @explicit.Error;
            }
            
            if (@explicit.Value.Metadata.State == SchemaLifecycleState.Retired)
            {
                _instrumentation.SchemaRetired(request.SchemaName, request.Version.Value);
                return Error.Conflict(ErrorCodes.SchemaRetired);
            }
            
            resolvedSchema = @explicit.Value;
        }
        else
        {
            var resolved =
                await registry.ResolveActiveSchemaAsync(request.SchemaName, submissionTime, cancellationToken);

            if (resolved.IsFailure) 
            {
                return resolved.Error;
            }

            resolvedSchema = resolved.Value;
        }
        
        var validation = await validator.ValidateAsync(request.PayloadJson, resolvedSchema.Schema, cancellationToken);
        
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        if (!validation.Value.IsValid)
        {
            _instrumentation.ContractViolation(
                request.SchemaName,
                resolvedSchema.Metadata.Version,
                validation.Value.Errors?.Count ?? 0);
            return Error.Validation(ErrorCodes.SchemaContractViolation, errors: validation.Value.Errors);
        }
        
        var urn = Urn.ExtractFromPayload(request.PayloadJson);
        
        if (urn.IsFailure)
        {
            _instrumentation.InvalidUrn(0);
            return Error.Validation(ErrorCodes.PayloadInvalidUrn);
        }
        
        var envelope = SubmissionEnvelope.Create(
            urn.Value,
            resolvedSchema.Metadata.Name,
            resolvedSchema.Metadata.Version,
            request.PayloadJson,
            submissionTime
        );
        
        var saveResult = await submissionInfrastructure.SaveSubmissionAsync(envelope, cancellationToken);
        
        if (saveResult.IsFailure)
        {
            _instrumentation.SubmissionSaveFailed(request.SchemaName, saveResult.Error.Description ?? string.Empty);
            return saveResult.Error;
        }

        _instrumentation.SubmissionSaved(
            envelope.SubmissionId,
            urn.Value,
            envelope.SchemaName,
            envelope.SchemaVersion,
            envelope.PayloadSha256Hash);

        return new AutoSubmissionResult(
            SubmissionId: envelope.SubmissionId,
            Urn: urn.Value,
            SchemaName: envelope.SchemaName,
            Version: envelope.SchemaVersion,
            PayloadHash: envelope.PayloadSha256Hash,
            SubmittedAtUtc: submissionTime
        );
    }
}
