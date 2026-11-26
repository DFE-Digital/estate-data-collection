using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Extensions;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.SubmitSchema;

public class SubmitSchemaUseCase(
    ISchemaRegistry schemaRegistry,
    ISchemaVerificationService schemaVerifier,
    SubmitSchemaUseCaseInstrumentation? instrumentation = null,
    IOdcsContractParser? odcsParser = null
) : IUseCase<SubmitSchemaResult, SubmitSchemaRequest>
{
    private readonly SubmitSchemaUseCaseInstrumentation _instrumentation =
        instrumentation ?? new SubmitSchemaUseCaseInstrumentation(NullLogger<SubmitSchemaUseCase>.Instance);

    public async Task<Result<SubmitSchemaResult>> ExecuteAsync(SubmitSchemaRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SchemaName) || request.Version <= 0)
        {
            return Error.Validation(
                ErrorCodes.SchemaInvalidSchedule,
                SchemaValidationMessages.SchemaNameAndPositiveVersionRequired);
        }

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

        var validFixtures = request.SampleValidFixtures ?? [];
        var invalidFixtures = request.SampleInvalidFixtures ?? [];

        if (request.IdentifierConfig is not null)
        {
            var idDefValidation = request.IdentifierConfig.ValidateDefinition();
            if (idDefValidation.IsFailure)
            {
                return await PersistInvalidRequestAsync(request, validFixtures, invalidFixtures, idDefValidation.Error, "identifierConfig", cancellationToken);
            }
        }

        if (request.ValidationPolicy is not null)
        {
            var policyValidation = request.ValidationPolicy.ValidateDefinition();
            if (policyValidation.IsFailure)
            {
                return await PersistInvalidRequestAsync(request, validFixtures, invalidFixtures, policyValidation.Error, "validationPolicy", cancellationToken);
            }
        }

        var resolvedInputs = odcsParser.ResolveInputs(
            request.SchemaJson,
            request.RulesJson,
            request.OdcsYaml,
            request.FileName);

        if (resolvedInputs.IsFailure)
        {
            return await PersistInvalidRequestAsync(request, validFixtures, invalidFixtures, resolvedInputs.Error, "odcs", cancellationToken);
        }

        var (effectiveSchemaJson, effectiveRulesJson, rawOdcsYaml, _, _, parsedValidationPolicy) = resolvedInputs.Value;
        var effectiveValidationPolicy = request.ValidationPolicy ?? parsedValidationPolicy;
        var rulesParam = string.IsNullOrWhiteSpace(effectiveRulesJson) ? null : effectiveRulesJson;

        var verification = await schemaVerifier.VerifyCandidateSchemaAsync(
            effectiveSchemaJson,
            rulesParam,
            validFixtures,
            invalidFixtures,
            request.IdentifierConfig,
            cancellationToken,
            effectiveValidationPolicy);

        if (verification.IsFailure)
        {
            return verification.Error;
        }

        var report = verification.Value;
        var effectiveIdentifierConfig = report.ResolvedIdentifierConfig ?? request.IdentifierConfig;

        if (!report.IsSuccessful)
        {
            _instrumentation.SchemaCandidateVerificationFailed(
                request.SchemaName,
                request.Version,
                report.Errors.Count,
                string.Join("; ", report.Errors));

            var invalidPersist = await SchemaCandidatePersistence.PersistFailedVerificationAsync(
                schemaRegistry,
                request.SchemaName,
                request.Version,
                effectiveSchemaJson ?? request.SchemaJson,
                effectiveRulesJson,
                rawOdcsYaml,
                validFixtures,
                invalidFixtures,
                effectiveIdentifierConfig,
                effectiveValidationPolicy,
                report,
                cancellationToken);

            if (invalidPersist.IsFailure)
            {
                return invalidPersist.Error;
            }

            var (invalidMeta, errorReport) = invalidPersist.Value;
            _instrumentation.SchemaSaved(invalidMeta.Name, invalidMeta.Version, invalidMeta.Sha256Hash);
            return ToResult(invalidMeta, hasErrorReport: true, errorReport.Issues);
        }

        SchemaMetadata metadata;
        SchemaErrorReport? warningReport;
        try
        {
            metadata = SchemaCandidatePersistence.CreateVerifiedMetadata(
                request.SchemaName,
                request.Version,
                effectiveSchemaJson ?? string.Empty,
                effectiveIdentifierConfig,
                effectiveValidationPolicy,
                report,
                out warningReport);
        }
        catch (Exception ex)
        {
            _instrumentation.SchemaInvalidSchedule(request.SchemaName, request.Version, ex.Message);
            return Error.Validation(ErrorCodes.SchemaInvalidSchedule, ex.Message);
        }

        var result = await SchemaCandidatePersistence.SaveWithArtifactsAsync(
            schemaRegistry,
            metadata,
            effectiveRulesJson,
            rawOdcsYaml,
            validFixtures,
            invalidFixtures,
            cancellationToken);

        if (result.IsFailure)
        {
            _instrumentation.SchemaSaveFailed(
                request.SchemaName,
                request.Version,
                result.Error.Description ?? string.Empty);
            return result.Error;
        }

        if (warningReport is not null)
        {
            var errSave = await schemaRegistry.SaveErrorReportAsync(warningReport, cancellationToken);
            if (errSave.IsFailure)
            {
                return errSave.Error;
            }
        }

        _instrumentation.SchemaSaved(metadata.Name, metadata.Version, metadata.Sha256Hash);
        return ToResult(metadata, metadata.HasErrorReport, warningReport?.Issues ?? report.Issues);
    }

    private async Task<Result<SubmitSchemaResult>> PersistInvalidRequestAsync(
        SubmitSchemaRequest request,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        Error error,
        string defaultPath,
        CancellationToken cancellationToken)
    {
        var normalised = SchemaCandidatePersistence.NormaliseError(error, defaultPath);
        _instrumentation.SchemaCandidateVerificationFailed(
            request.SchemaName,
            request.Version,
            normalised.Messages.Count,
            normalised.Summary);

        var persist = await SchemaCandidatePersistence.PersistInvalidCandidateAsync(
            schemaRegistry,
            request.SchemaName,
            request.Version,
            request.SchemaJson,
            request.RulesJson,
            request.OdcsYaml,
            validFixtures,
            invalidFixtures,
            request.IdentifierConfig,
            request.ValidationPolicy,
            error,
            defaultPath,
            cancellationToken);

        if (persist.IsFailure)
        {
            return persist.Error;
        }

        var (metadata, report) = persist.Value;
        _instrumentation.SchemaSaved(metadata.Name, metadata.Version, metadata.Sha256Hash);
        return ToResult(metadata, hasErrorReport: true, report.Issues);
    }

    private static SubmitSchemaResult ToResult(
        SchemaMetadata metadata,
        bool hasErrorReport,
        IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? issues)
    {
        return new SubmitSchemaResult(
            SchemaName: metadata.Name,
            Version: metadata.Version,
            State: metadata.State.ToNode(),
            Sha256Hash: metadata.Sha256Hash,
            EffectiveFromUtc: metadata.EffectiveFromUtc,
            EffectiveToUtc: metadata.EffectiveToUtc,
            IdentifierConfig: metadata.IdentifierConfig,
            ValidationPolicy: metadata.ValidationPolicy,
            HasErrorReport: hasErrorReport,
            ErrorSummary: metadata.ErrorSummary,
            Issues: issues);
    }
}
