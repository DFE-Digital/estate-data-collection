using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Extensions;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.VerifySchema;

public class VerifySchemaUseCase(
    ISchemaRegistry schemaRegistry,
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

        var verification = string.IsNullOrWhiteSpace(rulesInput) && !string.IsNullOrWhiteSpace(schemaInput)
            ? await schemaRegistry.VerifyCanditateSchemaAsync(
                schemaInput,
                null,
                request.SampleValidFixtures ?? [],
                request.SampleInvalidFixtures ?? [],
                cancellationToken)
            : await schemaRegistry.VerifyCanditateSchemaAsync(
                schemaInput,
                rulesInput,
                request.SampleValidFixtures ?? [],
                request.SampleInvalidFixtures ?? [],
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
        return new VerifySchemaResult("Schema verified successfully");
    }
}
