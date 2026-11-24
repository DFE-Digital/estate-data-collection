using System.Text.Json;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;

public sealed class UpdateSchemaTestsUseCase(
    ISchemaRegistry schemaRegistry,
    ISchemaVerificationService schemaVerifier,
    UpdateSchemaTestsUseCaseInstrumentation? instrumentation = null
) : IUseCase<UpdateSchemaTestsResult, UpdateSchemaTestsRequest>
{
    private readonly UpdateSchemaTestsUseCaseInstrumentation _instrumentation =
        instrumentation ?? new UpdateSchemaTestsUseCaseInstrumentation(NullLogger<UpdateSchemaTestsUseCase>.Instance);

    public async Task<Result<UpdateSchemaTestsResult>> ExecuteAsync(
        UpdateSchemaTestsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SchemaName) || request.Version < 1)
        {
            var validationErrors = new List<ValidationError>();
            if (string.IsNullOrWhiteSpace(request.SchemaName))
            {
                validationErrors.Add(new ValidationError("schemaName", "Schema name is required."));
            }

            if (request.Version < 1)
            {
                validationErrors.Add(new ValidationError("version", "Schema version must be greater than or equal to 1."));
            }

            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed,
                "Invalid schema identifier.",
                validationErrors);
        }

        var schemaName = request.SchemaName.Trim();
        var validFixtures = request.ValidFixtures ?? [];
        var invalidFixtures = request.InvalidFixtures ?? [];

        _instrumentation.UpdatingSchemaFixtures(schemaName, request.Version, validFixtures.Count, invalidFixtures.Count);

        var existingResult = await schemaRegistry.GetExplicitSchemaAsync(schemaName, request.Version, cancellationToken);
        if (existingResult.IsFailure)
        {
            return existingResult.Error;
        }

        var existing = existingResult.Value;
        var meta = existing.Metadata;

        var rulesJson = existing.RawRulesJson;
        if (string.IsNullOrWhiteSpace(rulesJson) && existing.Rules is { Count: > 0 })
        {
            rulesJson = JsonSerializer.Serialize(existing.Rules);
        }

        var verification = await schemaVerifier.VerifyCandidateSchemaAsync(
            meta.RawJsonSchema,
            rulesJson,
            validFixtures,
            invalidFixtures,
            meta.IdentifierConfig,
            cancellationToken);

        if (verification.IsFailure)
        {
            return verification.Error;
        }

        if (!verification.Value.IsSuccessful)
        {
            var errors = verification.Value.ToValidationErrors("schema").ToList();
            _instrumentation.SchemaFixtureUpdateVerificationFailed(
                meta.Name,
                meta.Version,
                errors.Count,
                string.Join("; ", verification.Value.Errors));

            return Error.Validation(ErrorCodes.SchemaVerificationFailed, errors: errors);
        }

        var updateFixturesResult = await schemaRegistry.UpdateSchemaFixturesAsync(
            meta.Name,
            meta.Version,
            validFixtures,
            invalidFixtures,
            cancellationToken);

        if (updateFixturesResult.IsFailure)
        {
            return updateFixturesResult.Error;
        }

        var now = DateTimeOffset.UtcNow;
        var appVersion = AppVersionProvider.InformativeVersion;
        var warnings = verification.Value.WarningMessages;
        var engineTraces = verification.Value.EngineTraces;
        meta.RecordTestRun(
            true,
            appVersion,
            errors: null,
            invalidFixtureOutcomes: verification.Value.InvalidFixtureOutcomes,
            warnings: warnings,
            engineTraces: ValidationEngineTraceMapper.ToDomainRecords(engineTraces));

        var metaUpdateResult = await schemaRegistry.UpdateSchemaMetadataAsync(meta, cancellationToken);
        if (metaUpdateResult.IsFailure)
        {
            return metaUpdateResult.Error;
        }

        _instrumentation.SchemaFixturesUpdated(meta.Name, meta.Version, validFixtures.Count, invalidFixtures.Count);

        return new UpdateSchemaTestsResult(
            SchemaName: meta.Name,
            Version: meta.Version,
            ValidFixtureCount: validFixtures.Count,
            InvalidFixtureCount: invalidFixtures.Count,
            BusinessRuleCount: existing.Rules?.Count ?? 0,
            ExecutedAtUtc: now,
            AppVersion: appVersion,
            InvalidFixtureOutcomes: verification.Value.InvalidFixtureOutcomes,
            Warnings: warnings,
            EngineTraces: engineTraces);
    }
}
