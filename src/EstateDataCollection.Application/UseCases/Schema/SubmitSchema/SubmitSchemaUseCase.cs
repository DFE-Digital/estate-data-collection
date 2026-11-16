using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Extensions;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.SubmitSchema;

public class SubmitSchemaUseCase(
    ISchemaRegistry schemaRegistry,
    SubmitSchemaUseCaseInstrumentation? instrumentation = null,
    IOdcsContractParser? odcsParser = null
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

        var resolvedInputs = odcsParser.ResolveInputs(
            request.SchemaJson,
            request.RulesJson,
            request.OdcsYaml,
            request.FileName);

        if (resolvedInputs.IsFailure)
        {
            return resolvedInputs.Error;
        }

        var (effectiveSchemaJson, effectiveRulesJson, rawOdcsYaml, _, _) = resolvedInputs.Value;

        var verification = string.IsNullOrWhiteSpace(effectiveRulesJson)
            ? await schemaRegistry.VerifyCanditateSchemaAsync(
                effectiveSchemaJson,
                null,
                request.SampleValidFixtures ?? [],
                request.SampleInvalidFixtures ?? [],
                cancellationToken)
            : await schemaRegistry.VerifyCanditateSchemaAsync(
                effectiveSchemaJson,
                effectiveRulesJson,
                request.SampleValidFixtures ?? [],
                request.SampleInvalidFixtures ?? [],
                cancellationToken);
        
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        if (!verification.Value.IsSuccessful)
        {
            var errors = verification.Value.ToValidationErrors("schema").ToList();

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
            metadata = SchemaMetadata.CreateDraft(
                request.SchemaName,
                request.Version,
                effectiveSchemaJson ?? string.Empty,
                request.IdentifierConfig);
            // Submitted schema packs are always stored in Validated state (unscheduled);
            // effective dates are assigned separately via the Admin scheduling API.
            metadata.MarkValidated();
        }
        catch (Exception ex)
        {
            _instrumentation.SchemaInvalidSchedule(request.SchemaName, request.Version, ex.Message);
            return Error.Validation(ErrorCodes.SchemaInvalidSchedule, ex.Message);
        }
        
        var result = effectiveRulesJson is not null || rawOdcsYaml is not null
            ? await schemaRegistry.SaveSchemaAsync(metadata, effectiveRulesJson, rawOdcsYaml, cancellationToken)
            : await schemaRegistry.SaveSchemaAsync(metadata, cancellationToken);

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
            EffectiveToUtc: metadata.EffectiveToUtc,
            IdentifierConfig: metadata.IdentifierConfig
        );
    }
}
