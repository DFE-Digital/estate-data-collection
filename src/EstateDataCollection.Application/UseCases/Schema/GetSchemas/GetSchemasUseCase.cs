using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.GetSchemas;

public sealed class GetSchemasUseCase(
    ISchemaRegistry schemaRegistry,
    GetSchemasUseCaseInstrumentation? instrumentation = null
) : IUseCase<GetSchemasResult, GetSchemasRequest>
{
    private readonly GetSchemasUseCaseInstrumentation _instrumentation =
        instrumentation ?? new GetSchemasUseCaseInstrumentation(NullLogger<GetSchemasUseCase>.Instance);

    public async Task<Result<GetSchemasResult>> ExecuteAsync(
        GetSchemasRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.ListingSchemaCatalogue(
            request.SchemaName,
            request.IncludeUnscheduled,
            request.IncludeUpcoming,
            request.IncludeRetired);

        var listResult = await schemaRegistry.ListSchemasAsync(request.SchemaName, cancellationToken);
        if (listResult.IsFailure)
        {
            return listResult.Error;
        }

        var now = DateTimeOffset.UtcNow;
        var filtered = listResult.Value
            .Where(m => m.IsVisibleForDiscovery(
                now,
                includeUnscheduled: request.IncludeUnscheduled,
                includeUpcoming: request.IncludeUpcoming,
                includeRetired: request.IncludeRetired))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.SchemaName) && filtered.Count == 0)
        {
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { SchemaName = request.SchemaName, Version = "any" });
        }

        var entries = filtered
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedVersions = group
                    .OrderByDescending(m => m.Version)
                    .ToList();

                var activeMeta = orderedVersions.FirstOrDefault(m => m.IsActiveAt(now));

                var versionSummaries = orderedVersions
                    .Select(m => new SchemaVersionSummary(
                        SchemaName: m.Name,
                        Version: m.Version,
                        State: m.GetEffectiveStateAt(now).ToNode(),
                        EffectiveFromUtc: m.EffectiveFromUtc,
                        EffectiveToUtc: m.EffectiveToUtc,
                        IsActiveNow: m.IsActiveAt(now),
                        CanAcceptTraffic: m.CanAcceptTrafficAt(now),
                        Sha256Hash: m.Sha256Hash,
                        CreatedAtUtc: m.CreatedAtUtc,
                        IdentifierConfig: m.IdentifierConfig,
                        LastTestRunAtUtc: m.LastTestRunAtUtc,
                        LastTestRunAppVersion: m.LastTestRunAppVersion,
                        LastTestRunPassed: m.LastTestRunPassed,
                        LastTestRunWarnings: m.LastTestRunWarnings,
                        HasErrorReport: m.HasErrorReport,
                        ErrorSummary: m.ErrorSummary,
                        SignedOffBy: m.SignedOffBy,
                        SignedOffAtUtc: m.SignedOffAtUtc,
                        DeletedAtUtc: m.DeletedAtUtc))
                    .ToList();

                var activeFields = request.IncludeActiveVersionFields && activeMeta is not null
                    ? JsonSchemaFieldExtractor.ExtractFields(activeMeta.RawJsonSchema)
                    : null;

                return new SchemaCatalogueEntry(
                    SchemaName: group.Key,
                    ActiveVersion: activeMeta?.Version,
                    Versions: versionSummaries,
                    ActiveVersionFields: activeFields);
            })
            .ToList();

        _instrumentation.SchemaCatalogueReturned(entries.Count);
        return new GetSchemasResult(entries);
    }
}
