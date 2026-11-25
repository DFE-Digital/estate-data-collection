using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema;

internal static class SchemaCandidatePersistence
{
    public sealed record NormalisedErrorDetails(
        IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>> Issues,
        IReadOnlyList<string> Messages,
        string Summary);

    public static NormalisedErrorDetails NormaliseError(Error error, string defaultPath)
    {
        var errs = error.ValidationErrors is { Count: > 0 }
            ? error.ValidationErrors.Select(e => e with { Severity = RuleSeverity.Error }).ToArray()
            : [new ValidationError(defaultPath, error.Description ?? error.Code, Severity: RuleSeverity.Error)];

        var issues = ValidationIssuesHelper.GroupBySeverity(errs);
        var messages = errs.Select(e => e.Message).ToArray();
        var summary = string.Join("; ", messages.Take(3));

        return new NormalisedErrorDetails(issues, messages, summary);
    }

    public static async Task<Result<(SchemaMetadata Metadata, SchemaErrorReport Report)>> PersistInvalidCandidateAsync(
        ISchemaRegistry schemaRegistry,
        string schemaName,
        int version,
        string? rawSchemaJson,
        string? rawRulesJson,
        string? rawOdcsYaml,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        SchemaIdentifierConfig? identifierConfig,
        SchemaValidationPolicy? validationPolicy,
        Error error,
        string defaultPath,
        CancellationToken cancellationToken)
    {
        var normalised = NormaliseError(error, defaultPath);

        var metadata = SchemaMetadata.CreateInvalid(
            schemaName,
            version,
            rawSchemaJson,
            identifierConfig,
            validationPolicy,
            normalised.Summary);

        var saveResult = await SaveWithArtifactsAsync(
            schemaRegistry,
            metadata,
            rawRulesJson,
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
            Issues: normalised.Issues,
            SyntaxErrors: normalised.Messages,
            FixtureErrors: [],
            Warnings: [],
            Summary: normalised.Summary);

        var reportSave = await schemaRegistry.SaveErrorReportAsync(report, cancellationToken);
        if (reportSave.IsFailure)
        {
            return reportSave.Error;
        }

        return (metadata, report);
    }

    public static async Task<Result<(SchemaMetadata Metadata, SchemaErrorReport ErrorReport)>> PersistFailedVerificationAsync(
        ISchemaRegistry schemaRegistry,
        string schemaName,
        int version,
        string? effectiveSchemaJson,
        string? effectiveRulesJson,
        string? rawOdcsYaml,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        SchemaIdentifierConfig? effectiveIdentifierConfig,
        SchemaValidationPolicy? effectiveValidationPolicy,
        SchemaVerificationResult report,
        CancellationToken cancellationToken)
    {
        var errorReport = report.ToErrorReport(schemaName, version);
        var invalidMeta = SchemaMetadata.CreateInvalid(
            schemaName,
            version,
            effectiveSchemaJson,
            effectiveIdentifierConfig,
            effectiveValidationPolicy,
            errorReport.Summary);

        var saveInvalidResult = await SaveWithArtifactsAsync(
            schemaRegistry,
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

        return (invalidMeta, errorReport);
    }

    public static SchemaMetadata CreateVerifiedMetadata(
        string schemaName,
        int version,
        string effectiveSchemaJson,
        SchemaIdentifierConfig? effectiveIdentifierConfig,
        SchemaValidationPolicy? effectiveValidationPolicy,
        SchemaVerificationResult report,
        out SchemaErrorReport? warningReport)
    {
        var metadata = SchemaMetadata.CreateDraft(
            schemaName,
            version,
            effectiveSchemaJson,
            effectiveIdentifierConfig,
            effectiveValidationPolicy);

        if (report.HasWarnings)
        {
            warningReport = report.ToErrorReport(schemaName, version);
            metadata.MarkUnvalidated(warningReport.Summary);
        }
        else
        {
            warningReport = null;
            metadata.MarkValidated();
        }

        return metadata;
    }

    public static Task<Result> SaveWithArtifactsAsync(
        ISchemaRegistry schemaRegistry,
        SchemaMetadata metadata,
        string? rulesJson,
        string? rawOdcsYaml,
        IReadOnlyList<string> validFixtures,
        IReadOnlyList<string> invalidFixtures,
        CancellationToken cancellationToken)
    {
        var hasFixtures = validFixtures.Count > 0 || invalidFixtures.Count > 0;
        if (hasFixtures)
        {
            return schemaRegistry.SaveSchemaAsync(
                metadata,
                rulesJson,
                rawOdcsYaml,
                validFixtures,
                invalidFixtures,
                cancellationToken);
        }

        return rulesJson is not null || rawOdcsYaml is not null
            ? schemaRegistry.SaveSchemaAsync(metadata, rulesJson, rawOdcsYaml, cancellationToken)
            : schemaRegistry.SaveSchemaAsync(metadata, cancellationToken);
    }
}
