using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Extensions;
using EstateDataCollection.Common.Messages;
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
                return BuildErrorReportResult(idDefValidation.Error, nameof(request.IdentifierConfig));
            }
        }

        if (request.ValidationPolicy is not null)
        {
            var policyValidation = request.ValidationPolicy.ValidateDefinition();
            if (policyValidation.IsFailure)
            {
                return BuildErrorReportResult(policyValidation.Error, nameof(request.ValidationPolicy));
            }
        }

        var resolvedInputs = odcsParser.ResolveInputs(
            request.SchemaJson,
            request.RulesJson,
            request.OdcsYaml,
            request.FileName);

        if (resolvedInputs.IsFailure)
        {
            return BuildErrorReportResult(resolvedInputs.Error, "odcs");
        }

        var (schemaInput, rulesInput, _, _, _, parsedValidationPolicy) = resolvedInputs.Value;
        var effectivePolicy = request.ValidationPolicy ?? parsedValidationPolicy;
        var effectiveRulesInput = string.IsNullOrWhiteSpace(rulesInput) && !string.IsNullOrWhiteSpace(schemaInput)
            ? null
            : rulesInput;

        var verification = await schemaVerifier.VerifyCandidateSchemaAsync(
            schemaInput,
            effectiveRulesInput,
            request.SampleValidFixtures ?? [],
            request.SampleInvalidFixtures ?? [],
            request.IdentifierConfig,
            cancellationToken,
            effectivePolicy,
            request.AmbientFixtures);

        if (verification.IsFailure)
        {
            _instrumentation.SchemaVerificationRegistryError(
                verification.Error.Code,
                verification.Error.Description ?? string.Empty);
            return verification.Error;
        }

        var report = verification.Value;
        if (!report.IsSuccessful)
        {
            var errors = report.ToValidationErrors("schema").ToList();
            _instrumentation.SchemaVerificationFailed(
                errors.Count,
                string.Join("; ", report.Errors));

            return new VerifySchemaResult(
                Message: SchemaValidationMessages.SchemaVerificationFoundErrors(errors.Count),
                ResolvedIdentifierConfig: report.ResolvedIdentifierConfig,
                InvalidFixtureOutcomes: report.InvalidFixtureOutcomes,
                Warnings: report.WarningMessages,
                EngineTraces: report.EngineTraces,
                IsSuccessful: false,
                Issues: report.Issues,
                SyntaxErrors: report.SyntaxErrors.Select(e => e.Message).ToArray(),
                FixtureErrors: report.FixtureErrors.Select(e => e.Message).ToArray(),
                AnalyserFindings: report.AnalyserFindings);
        }

        _instrumentation.SchemaVerificationSucceeded();
        return new VerifySchemaResult(
            Message: SchemaValidationMessages.SchemaVerifiedSuccessfully,
            ResolvedIdentifierConfig: report.ResolvedIdentifierConfig,
            InvalidFixtureOutcomes: report.InvalidFixtureOutcomes,
            Warnings: report.WarningMessages,
            EngineTraces: report.EngineTraces,
            IsSuccessful: true,
            Issues: report.Issues,
            SyntaxErrors: [],
            FixtureErrors: [],
            AnalyserFindings: report.AnalyserFindings);
    }

    private static VerifySchemaResult BuildErrorReportResult(Error error, string defaultPath)
    {
        var normalised = SchemaCandidatePersistence.NormaliseError(error, defaultPath);

        return new VerifySchemaResult(
            Message: SchemaValidationMessages.SchemaVerificationFoundErrors(normalised.Messages.Count),
            Warnings: [],
            IsSuccessful: false,
            Issues: normalised.Issues,
            SyntaxErrors: normalised.Messages,
            FixtureErrors: []);
    }
}
