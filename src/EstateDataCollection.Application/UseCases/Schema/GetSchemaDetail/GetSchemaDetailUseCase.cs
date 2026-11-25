using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;

public sealed class GetSchemaDetailUseCase(
    ISchemaRegistry schemaRegistry,
    GetSchemaDetailUseCaseInstrumentation? instrumentation = null,
    IPayloadValidationService? validationService = null
) : IUseCase<SchemaVersionDetail, GetSchemaDetailRequest>
{
    private readonly GetSchemaDetailUseCaseInstrumentation _instrumentation =
        instrumentation ?? new GetSchemaDetailUseCaseInstrumentation(NullLogger<GetSchemaDetailUseCase>.Instance);

    public async Task<Result<SchemaVersionDetail>> ExecuteAsync(
        GetSchemaDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.FetchingSchemaDetail(
            request.SchemaName,
            request.Version,
            request.IncludeUnscheduled,
            request.IncludeUpcoming,
            request.IncludeRetired);

        var resolvedResult = await schemaRegistry.GetExplicitSchemaAsync(
            request.SchemaName,
            request.Version,
            cancellationToken);

        if (resolvedResult.IsFailure)
        {
            return resolvedResult.Error;
        }

        var now = DateTimeOffset.UtcNow;
        var metadata = resolvedResult.Value.Metadata;
        if (!metadata.IsVisibleForDiscovery(
                now,
                includeUnscheduled: request.IncludeUnscheduled,
                includeUpcoming: request.IncludeUpcoming,
                includeRetired: request.IncludeRetired))
        {
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { request.SchemaName, request.Version });
        }

        var fields = JsonSchemaFieldExtractor.ExtractFields(metadata.RawJsonSchema);
        var rules = (resolvedResult.Value.Rules ?? [])
            .Select(r => new SchemaQualityRuleSummary(
                Id: r.Id,
                Dimension: r.Type,
                Severity: r.Severity.ToKeyString().ToUpperInvariant(),
                TargetPath: r.Path,
                Engine: r.ResolveEngine(),
                ErrorMessage: r.EffectiveMessage,
                Description: r.Description))
            .ToList();

        var lastRunEngineTraces = ValidationEngineTraceMapper.ToApplicationTraces(metadata.LastTestRunEngineTraces);
        var registeredEngines = validationService?.GetRegisteredEngines() ?? [];

        return new SchemaVersionDetail(
            SchemaName: metadata.Name,
            Version: metadata.Version,
            State: metadata.GetEffectiveStateAt(now).ToNode(),
            EffectiveFromUtc: metadata.EffectiveFromUtc,
            EffectiveToUtc: metadata.EffectiveToUtc,
            IsActiveNow: metadata.IsActiveAt(now),
            CanAcceptTraffic: metadata.CanAcceptTrafficAt(now),
            Sha256Hash: metadata.Sha256Hash,
            CreatedAtUtc: metadata.CreatedAtUtc,
            Fields: fields,
            QualityRules: rules,
            RawJsonSchema: request.IncludeRawJsonSchema ? metadata.RawJsonSchema : null,
            IdentifierConfig: metadata.IdentifierConfig,
            ValidFixtures: resolvedResult.Value.ValidFixtures ?? [],
            InvalidFixtures: resolvedResult.Value.InvalidFixtures ?? [],
            LastTestRunAtUtc: metadata.LastTestRunAtUtc,
            LastTestRunAppVersion: metadata.LastTestRunAppVersion,
            LastTestRunPassed: metadata.LastTestRunPassed,
            LastTestRunErrors: metadata.LastTestRunErrors ?? [],
            LastTestRunInvalidFixtures: metadata.LastTestRunInvalidFixtures ?? [],
            LastTestRunWarnings: metadata.LastTestRunWarnings ?? [],
            LastTestRunEngineTraces: lastRunEngineTraces,
            RegisteredEngines: registeredEngines,
            ValidationPolicy: metadata.ValidationPolicy);
    }
}
