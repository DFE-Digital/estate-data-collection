using System.Text.Json;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;

public sealed class RunSchemaTestsUseCase(
    ISchemaRegistry schemaRegistry,
    ISchemaVerificationService schemaVerifier,
    RunSchemaTestsUseCaseInstrumentation? instrumentation = null
) : IUseCase<RunSchemaTestsResult, RunSchemaTestsRequest>
{
    private readonly RunSchemaTestsUseCaseInstrumentation _instrumentation =
        instrumentation ?? new RunSchemaTestsUseCaseInstrumentation(NullLogger<RunSchemaTestsUseCase>.Instance);

    public async Task<Result<RunSchemaTestsResult>> ExecuteAsync(
        RunSchemaTestsRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaFilter = string.IsNullOrWhiteSpace(request.SchemaName) ? null : request.SchemaName.Trim();
        _instrumentation.RunningSchemaTests(schemaFilter ?? "*", request.Version, request.IncludeRetired);

        var targets = new List<ResolvedSchema>();

        if (schemaFilter is not null && request.Version is > 0)
        {
            var singleResult = await schemaRegistry.GetExplicitSchemaAsync(
                schemaFilter,
                request.Version.Value,
                cancellationToken);

            if (singleResult.IsFailure)
            {
                return singleResult.Error;
            }

            if (singleResult.Value.Metadata.IsDeleted)
            {
                return Error.Validation(ErrorCodes.SchemaDeleted, new { SchemaName = schemaFilter, Version = request.Version.Value });
            }

            targets.Add(singleResult.Value);
        }
        else
        {
            var listResult = await schemaRegistry.ListSchemasAsync(schemaFilter, cancellationToken);
            if (listResult.IsFailure)
            {
                return listResult.Error;
            }

            var matchingMetadata = listResult.Value
                .Where(m => !m.IsDeleted)
                .Where(m => request.IncludeRetired || m.State != SchemaLifecycleState.Retired)
                .Where(m => request.Version is null || m.Version == request.Version.Value)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Version)
                .ToList();

            if ((schemaFilter is not null || request.Version is not null) && matchingMetadata.Count == 0)
            {
                return Error.NotFound(ErrorCodes.SchemaNotFound, new
                {
                    SchemaName = schemaFilter ?? "any",
                    Version = request.Version?.ToString() ?? "any"
                });
            }

            foreach (var meta in matchingMetadata)
            {
                var explicitResult = await schemaRegistry.GetExplicitSchemaAsync(meta.Name, meta.Version, cancellationToken);
                if (explicitResult.IsSuccess)
                {
                    targets.Add(explicitResult.Value);
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var appVersion = AppVersionProvider.InformativeVersion;
        var itemResults = new List<SchemaTestSuiteItemResult>(targets.Count);

        foreach (var resolved in targets)
        {
            var meta = resolved.Metadata;
            var validFixtures = resolved.ValidFixtures ?? [];
            var invalidFixtures = resolved.InvalidFixtures ?? [];

            var rulesJson = resolved.RawRulesJson;
            if (string.IsNullOrWhiteSpace(rulesJson) && resolved.Rules is { Count: > 0 })
            {
                rulesJson = JsonSerializer.Serialize(resolved.Rules);
            }

            var verification = await schemaVerifier.VerifyCandidateSchemaAsync(
                meta.RawJsonSchema,
                rulesJson,
                validFixtures,
                invalidFixtures,
                meta.IdentifierConfig,
                cancellationToken,
                meta.ValidationPolicy);

            bool passed;
            IReadOnlyList<string> errors;
            IReadOnlyList<string> warnings;

            if (verification.IsFailure)
            {
                passed = false;
                errors = [verification.Error.Description ?? verification.Error.Code];
                warnings = [];
            }
            else
            {
                passed = verification.Value.IsSuccessful;
                errors = verification.Value.Errors;
                warnings = verification.Value.WarningMessages;
            }

            if (!passed)
            {
                _instrumentation.SchemaTestFailed(meta.Name, meta.Version, errors.Count, string.Join("; ", errors));
            }

            var invalidOutcomes = verification.IsSuccess
                ? verification.Value.InvalidFixtureOutcomes
                : null;
            var engineTraces = verification.IsSuccess
                ? verification.Value.EngineTraces
                : null;

            meta.RecordTestRun(
                passed,
                appVersion,
                errors,
                invalidOutcomes,
                warnings,
                ValidationEngineTraceMapper.ToDomainRecords(engineTraces));

            if (verification.IsSuccess && (!passed || verification.Value.HasWarnings))
            {
                var errorReport = verification.Value.ToErrorReport(meta.Name, meta.Version);
                meta.UpdateErrorReportStatus(true, errorReport.Summary);
                _ = await schemaRegistry.SaveErrorReportAsync(errorReport, cancellationToken);
            }
            else if (passed && meta.State != SchemaLifecycleState.Unvalidated && meta.State != SchemaLifecycleState.Invalid)
            {
                meta.UpdateErrorReportStatus(false, null);
            }

            var updateResult = await schemaRegistry.UpdateSchemaMetadataAsync(meta, cancellationToken);
            if (updateResult.IsFailure)
            {
                return updateResult.Error;
            }

            var effectiveId = verification.IsSuccess
                ? (verification.Value.ResolvedIdentifierConfig ?? meta.IdentifierConfig)
                : meta.IdentifierConfig;

            itemResults.Add(new SchemaTestSuiteItemResult(
                SchemaName: meta.Name,
                Version: meta.Version,
                State: meta.GetEffectiveStateAt(now).ToNode().Name,
                Passed: passed,
                HasFixtures: validFixtures.Count > 0 || invalidFixtures.Count > 0,
                ValidFixtureCount: validFixtures.Count,
                InvalidFixtureCount: invalidFixtures.Count,
                BusinessRuleCount: resolved.Rules?.Count ?? 0,
                Errors: errors,
                IdentifierType: effectiveId?.Type,
                IdentifierPointer: effectiveId?.JsonPointer,
                InvalidFixtureOutcomes: invalidOutcomes,
                Warnings: warnings,
                EngineTraces: engineTraces));
        }

        var passedCount = itemResults.Count(r => r.Passed);
        var failedCount = itemResults.Count - passedCount;

        _instrumentation.SchemaTestsCompleted(appVersion, itemResults.Count, passedCount, failedCount);

        return new RunSchemaTestsResult(
            ExecutedAtUtc: now,
            AppVersion: appVersion,
            TotalSchemasChecked: itemResults.Count,
            PassedCount: passedCount,
            FailedCount: failedCount,
            Results: itemResults);
    }
}
