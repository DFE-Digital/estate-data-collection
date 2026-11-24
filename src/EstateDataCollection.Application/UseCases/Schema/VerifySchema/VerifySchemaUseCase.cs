using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Extensions;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.VerifySchema;

public class VerifySchemaUseCase(
    ISchemaVerificationService schemaVerifier,
    VerifySchemaUseCaseInstrumentation? instrumentation = null,
    IOdcsContractParser? odcsParser = null
) : IUseCase<VerifySchemaResult, VerifySchemaRequest>
{
    private readonly VerifySchemaUseCaseInstrumentation _instrumentation =
        instrumentation ?? new VerifySchemaUseCaseInstrumentation(NullLogger<VerifySchemaUseCase>.Instance);

    public async Task<Result<VerifySchemaResult>> ExecuteAsync(VerifySchemaRequest request, CancellationToken cancellationToken = default)
    {
        _instrumentation.VerifyingCandidateSchema(
            request.SampleValidFixtures?.Count ?? 0,
            request.SampleInvalidFixtures?.Count ?? 0);

        if (request.IdentifierConfig is not null)
        {
            var idDefValidation = request.IdentifierConfig.ValidateDefinition();
            if (idDefValidation.IsFailure)
            {
                return idDefValidation.Error;
            }
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

        var (schemaInput, rulesInput, _, _, _) = resolvedInputs.Value;
        var effectiveRulesInput = string.IsNullOrWhiteSpace(rulesInput) && !string.IsNullOrWhiteSpace(schemaInput)
            ? null
            : rulesInput;
        var validFixtures = request.SampleValidFixtures ?? [];
        var invalidFixtures = request.SampleInvalidFixtures ?? [];

        var verification = await schemaVerifier.VerifyCandidateSchemaAsync(
            schemaInput,
            effectiveRulesInput,
            validFixtures,
            invalidFixtures,
            request.IdentifierConfig,
            cancellationToken);

        if (verification.IsFailure)
        {
            _instrumentation.SchemaVerificationRegistryError(
                verification.Error.Code,
                verification.Error.Description ?? string.Empty);
            return verification.Error;
        }

        if (!verification.Value.IsSuccessful)
        {
            var errors = verification.Value.ToValidationErrors("schema").ToList();

            _instrumentation.SchemaVerificationFailed(
                errors.Count,
                string.Join("; ", verification.Value.Errors));

            return Error.Validation(ErrorCodes.SchemaVerificationFailed, errors: errors);
        }

        _instrumentation.SchemaVerificationSucceeded();
        return new VerifySchemaResult(
            "Schema verified successfully",
            verification.Value.ResolvedIdentifierConfig,
            verification.Value.InvalidFixtureOutcomes,
            verification.Value.WarningMessages);
    }
}
