using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.SubmitSchema;

public class SubmitSchemaUseCase(
    ISchemaRegistry schemaRegistry,
    SubmitSchemaUseCaseInstrumentation? instrumentation = null
) : IUseCase<SubmitSchemaResult, SubmitSchemaRequest>
{
    private readonly SubmitSchemaUseCaseInstrumentation _instrumentation =
        instrumentation ?? new SubmitSchemaUseCaseInstrumentation(NullLogger<SubmitSchemaUseCase>.Instance);

    public async Task<Result<SubmitSchemaResult>> ExecuteAsync(SubmitSchemaRequest request, CancellationToken cancellationToken = default)
    {
        _instrumentation.SubmittingSchema(request.SchemaName, request.Version);

        var exists = await schemaRegistry.ExistsAsync(request.SchemaName, request.Version, cancellationToken);

        if (exists.IsFailure)
        {
            return exists.Error;
        }

        if (exists.Value)
        {
            _instrumentation.SchemaAlreadyExists(request.SchemaName, request.Version);
            return Error.Conflict(ErrorCodes.SchemaAlreadyExists, new { request.SchemaName, request.Version });
        }

        var verification = await schemaRegistry.VerifyCanditateSchemaAsync(
            request.SchemaJson,
            request.SampleValidFixtures ?? [],
            request.SampleInvalidFixtures ?? [],
            cancellationToken);
        
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        if (!verification.Value.IsSuccessful)
        {
            var errors = verification.Value.Errors
                .Select(x => new ValidationError("schema", x))
                .ToList();

            _instrumentation.SchemaCandidateVerificationFailed(
                request.SchemaName,
                request.Version,
                errors.Count,
                string.Join("; ", verification.Value.Errors));

            return Error.Validation(ErrorCodes.SchemaVerificationFailed, errors: errors);
        }
        
        SchemaMetadata metadata;
        try
        {
            metadata = SchemaMetadata.CreateDraft(request.SchemaName, request.Version, request.SchemaJson);
            metadata.ScheduleActivation(request.EffectiveFromUtc, request.EffectiveToUtc);
        }
        catch (Exception ex)
        {
            _instrumentation.SchemaInvalidSchedule(request.SchemaName, request.Version, ex.Message);
            return Error.Validation(ErrorCodes.SchemaInvalidSchedule, ex.Message);
        }
        
        var result = await schemaRegistry.SaveSchemaAsync(metadata, cancellationToken);

        if (result.IsFailure)
        {
            _instrumentation.SchemaSaveFailed(
                request.SchemaName,
                request.Version,
                result.Error.Description ?? string.Empty);
            return result.Error;
        }
        
        _instrumentation.SchemaSaved(metadata.Name, metadata.Version, metadata.Sha256Hash);

        return new SubmitSchemaResult(
            SchemaName: metadata.Name,
            Version: metadata.Version,
            State: metadata.State.ToNode(),
            Sha256Hash: metadata.Sha256Hash,
            EffectiveFromUtc: metadata.EffectiveFromUtc,
            EffectiveToUtc: metadata.EffectiveToUtc
        );
    }
}
