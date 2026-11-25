using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Extensions;
using EstateDataCollection.Common;
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
                "SchemaName and positive Version are required.");
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
                return await PersistInvalidSubmissionAsync(
                    request,
                    effectiveSchemaJson: request.SchemaJson,
                    effectiveRulesJson: request.RulesJson,
                    rawOdcsYaml: request.OdcsYaml,
                    validFixtures,
                    invalidFixtures,
                    request.IdentifierConfig,
                    request.ValidationPolicy,
                    idDefValidation.Error,
                    "identifierConfig",
                    cancellationToken);
            }
        }

        if (request.ValidationPolicy is not null)
        {
            var policyValidation = request.ValidationPolicy.ValidateDefinition();
            if (policyValidation.IsFailure)
            {
                return await PersistInvalidSubmissionAsync(
                    request,
                    effectiveSchemaJson: request.SchemaJson,
                    effectiveRulesJson: request.RulesJson,
                    rawOdcsYaml: request.OdcsYaml,
                    validFixtures,
                    invalidFixtures,
                    request.IdentifierConfig,
                    request.ValidationPolicy,
                    policyValidation.Error,
                    "validationPolicy",
                    cancellationToken);
            }
        }

        var resolvedInputs = odcsParser.ResolveInputs(
            request.SchemaJson,
            request.RulesJson,
            request.OdcsYaml,
            request.FileName);

        if (resolvedInputs.IsFailure)
        {
            return await PersistInvalidSubmissionAsync(
                request,
                effectiveSchemaJson: request.SchemaJson,
                effectiveRulesJson: request.RulesJson,
                rawOdcsYaml: request.OdcsYaml,
                validFixtures,
                invalidFixtures,
                request.IdentifierConfig,
                request.ValidationPolicy,
                resolvedInputs.Error,
                "odcs",
                cancellationToken);
        }

        var (effectiveSchemaJson, effectiveRulesJson, rawOdcsYaml, _, _, parsedValidationPolicy) = resolvedInputs.Value;
        var effectiveValidationPolicy = request.ValidationPolicy ?? parsedValidationPolicy;
        var rulesParam = string.IsNullOrWhiteSpace(effectiveRulesJson) ? null : effectiveRulesJson;

        var verification = effectiveValidationPolicy is not null
            ? await schemaVerifier.VerifyCandidateSchemaAsync(
                effectiveSchemaJson,
                rulesParam,
                validFixtures,
                invalidFixtures,
                request.IdentifierConfig,
                cancellationToken,
                effectiveValidationPolicy)
            : await schemaVerifier.VerifyCandidateSchemaAsync(
                effectiveSchemaJson,
                rulesParam,
                validFixtures,
                invalidFixtures,
                request.IdentifierConfig,
                cancellationToken);

        if (verification.IsFailure)
        {
            return verification.Error;
        }

        var report = verification.Value;
        var effectiveIdentifierConfig = report.ResolvedIdentifierConfig ?? request.IdentifierConfig;

        if (!report.IsSuccessful)
        {
            var errorReport = report.ToErrorReport(request.SchemaName, request.Version);
            _instrumentation.SchemaCandidateVerificationFailed(
                request.SchemaName,
                request.Version,
                report.Errors.Count,
                string.Join("; ", report.Errors));

            var invalidMeta = SchemaMetadata.CreateInvalid(
                request.SchemaName,
                request.Version,
                effectiveSchemaJson ?? request.SchemaJson,
                effectiveIdentifierConfig,
                effectiveValidationPolicy,
                errorReport.Summary);

            var saveInvalidResult = await SaveWithFixturesAsync(
                invalidMeta,
                effectiveRulesJson,
                rawOdcsYaml,
                validFixtures,
                invalidFixtures,
                cancellationToken);

            if (saveInvalidResult.IsFailure)
            {
                return saveInvalidResult.Error;
            }

            var saveErrorResult = await schemaRegistry.SaveErrorReportAsync(errorReport, cancellationToken);
            if (saveErrorResult.IsFailure)
            {
                return saveErrorResult.Error;
            }

            _instrumentation.SchemaSaved(invalidMeta.Name, invalidMeta.Version, invalidMeta.Sha256Hash);
            return new SubmitSchemaResult(
                SchemaName: invalidMeta.Name,
                Version: invalidMeta.Version,
                State: invalidMeta.State.ToNode(),
                Sha256Hash: invalidMeta.Sha256Hash,
                EffectiveFromUtc: invalidMeta.EffectiveFromUtc,
                EffectiveToUtc: invalidMeta.EffectiveToUtc,
                IdentifierConfig: invalidMeta.IdentifierConfig,
                ValidationPolicy: invalidMeta.ValidationPolicy,
                HasErrorReport: true,
                ErrorSummary: invalidMeta.ErrorSummary,
                Issues: errorReport.Issues);
        }

        SchemaMetadata metadata;
        SchemaErrorReport? warningReport = null;
        try
        {
            metadata = SchemaMetadata.CreateDraft(
                request.SchemaName,
                request.Version,
                effectiveSchemaJson ?? string.Empty,
                effectiveIdentifierConfig,
                effectiveValidationPolicy);

            if (report.HasWarnings)
            {
                warningReport = report.ToErrorReport(request.SchemaName, request.Version);
                metadata.MarkUnvalidated(warningReport.Summary);
            }
            else
            {
                metadata.MarkValidated();
            }
        }
        catch (Exception ex)
        {
            _instrumentation.SchemaInvalidSchedule(request.SchemaName, request.Version, ex.Message);
            return Error.Validation(ErrorCodes.SchemaInvalidSchedule, ex.Message);
        }

        var result = await SaveWithFixturesAsync(
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

        return new SubmitSchemaResult(
            SchemaName: metadata.Name,
            Version: metadata.Version,
            State: metadata.State.ToNode(),
            Sha256Hash: metadata.Sha256Hash,
            EffectiveFromUtc: metadata.EffectiveFromUtc,
            EffectiveToUtc: metadata.EffectiveToUtc,
            IdentifierConfig: metadata.IdentifierConfig,
            ValidationPolicy: metadata.ValidationPolicy,
            HasErrorReport: metadata.HasErrorReport,
            ErrorSummary: metadata.ErrorSummary,
            Issues: warningReport?.Issues ?? report.Issues);
    }

    private async Task<Result<SubmitSchemaResult>> PersistInvalidSubmissionAsync(
        SubmitSchemaRequest request,
        string? effectiveSchemaJson,
        string? effectiveRulesJson,
        string? rawOdcsYaml,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        SchemaIdentifierConfig? identifierConfig,
        SchemaValidationPolicy? validationPolicy,
        Error error,
        string defaultPath,
        CancellationToken cancellationToken)
    {
        var errs = error.ValidationErrors is { Count: > 0 }
            ? error.ValidationErrors.Select(e => e with { Severity = RuleSeverity.Error }).ToArray()
            : [new ValidationError(defaultPath, error.Description ?? error.Code, Severity: RuleSeverity.Error)];

        var issues = ValidationIssuesHelper.GroupBySeverity(errs);
        var messages = errs.Select(e => e.Message).ToArray();
        var summary = string.Join("; ", messages.Take(3));

        _instrumentation.SchemaCandidateVerificationFailed(
            request.SchemaName,
            request.Version,
            errs.Length,
            summary);

        var metadata = SchemaMetadata.CreateInvalid(
            request.SchemaName,
            request.Version,
            effectiveSchemaJson,
            identifierConfig,
            validationPolicy,
            summary);

        var saveResult = await SaveWithFixturesAsync(
            metadata,
            effectiveRulesJson,
            rawOdcsYaml,
            validFixtures,
            invalidFixtures,
            cancellationToken);

        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        var report = new SchemaErrorReport(
            SchemaName: metadata.Name,
            Version: metadata.Version,
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Issues: issues,
            SyntaxErrors: messages,
            FixtureErrors: [],
            Warnings: [],
            Summary: summary);

        var reportSave = await schemaRegistry.SaveErrorReportAsync(report, cancellationToken);
        if (reportSave.IsFailure)
        {
            return reportSave.Error;
        }

        _instrumentation.SchemaSaved(metadata.Name, metadata.Version, metadata.Sha256Hash);
        return new SubmitSchemaResult(
            SchemaName: metadata.Name,
            Version: metadata.Version,
            State: metadata.State.ToNode(),
            Sha256Hash: metadata.Sha256Hash,
            EffectiveFromUtc: metadata.EffectiveFromUtc,
            EffectiveToUtc: metadata.EffectiveToUtc,
            IdentifierConfig: metadata.IdentifierConfig,
            ValidationPolicy: metadata.ValidationPolicy,
            HasErrorReport: true,
            ErrorSummary: metadata.ErrorSummary,
            Issues: issues);
    }

    private Task<Result> SaveWithFixturesAsync(
        SchemaMetadata metadata,
        string? effectiveRulesJson,
        string? rawOdcsYaml,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        CancellationToken cancellationToken)
    {
        var hasFixtures = validFixtures.Count > 0 || invalidFixtures.Count > 0;
        return hasFixtures
            ? schemaRegistry.SaveSchemaAsync(metadata, effectiveRulesJson, rawOdcsYaml, validFixtures, invalidFixtures, cancellationToken)
            : (effectiveRulesJson is not null || rawOdcsYaml is not null
                ? schemaRegistry.SaveSchemaAsync(metadata, effectiveRulesJson, rawOdcsYaml, cancellationToken)
                : schemaRegistry.SaveSchemaAsync(metadata, cancellationToken));
    }
}
