using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.VerifySchema;

public class VerifySchemaUseCase(
    ISchemaRegistry schemaRegistry,
    VerifySchemaUseCaseInstrumentation? instrumentation = null
) : IUseCase<VerifySchemaResult, VerifySchemaRequest>
{
    private readonly VerifySchemaUseCaseInstrumentation _instrumentation =
        instrumentation ?? new VerifySchemaUseCaseInstrumentation(NullLogger<VerifySchemaUseCase>.Instance);

    public async Task<Result<VerifySchemaResult>> ExecuteAsync(VerifySchemaRequest request, CancellationToken cancellationToken = default)
    {
        _instrumentation.VerifyingCandidateSchema(
            request.SampleValidFixtures?.Count ?? 0,
            request.SampleInvalidFixtures?.Count ?? 0);

        var verification = await schemaRegistry.VerifyCanditateSchemaAsync(
            request.SchemaJson,
            request.SampleValidFixtures,
            request.SampleInvalidFixtures,
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
            var errors = verification.Value.Errors
                .Select(x => new ValidationError("schema", x))
                .ToList();

            _instrumentation.SchemaVerificationFailed(
                errors.Count,
                string.Join("; ", verification.Value.Errors));

            return Error.Validation(ErrorCodes.SchemaVerificationFailed, errors: errors);
        }

        _instrumentation.SchemaVerificationSucceeded();
        return new VerifySchemaResult("Schema verified successfully");
    }
}
