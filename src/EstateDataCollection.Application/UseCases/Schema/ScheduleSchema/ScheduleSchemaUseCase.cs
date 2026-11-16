using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;

public sealed class ScheduleSchemaUseCase(
    ISchemaRegistry schemaRegistry,
    ScheduleSchemaUseCaseInstrumentation? instrumentation = null
) : IUseCase<ScheduleSchemaResult, ScheduleSchemaRequest>
{
    private readonly ScheduleSchemaUseCaseInstrumentation _instrumentation =
        instrumentation ?? new ScheduleSchemaUseCaseInstrumentation(NullLogger<ScheduleSchemaUseCase>.Instance);

    public async Task<Result<ScheduleSchemaResult>> ExecuteAsync(
        ScheduleSchemaRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.UpdatingSchemaSchedule(request.SchemaName, request.Version);

        var existing = await schemaRegistry.GetExplicitSchemaAsync(request.SchemaName, request.Version, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var metadata = existing.Value.Metadata;
        if (metadata.IsRetired)
        {
            _instrumentation.SchemaScheduleFailed(request.SchemaName, request.Version, "Schema is already retired.");
            return Error.Validation(ErrorCodes.SchemaRetired, new { request.SchemaName, request.Version });
        }

        try
        {
            if (request.IdentifierConfig is not null)
            {
                metadata.ConfigureIdentifier(request.IdentifierConfig);
            }

            if (!string.IsNullOrWhiteSpace(request.LifecycleAction))
            {
                var action = request.LifecycleAction.Trim();
                if (string.Equals(action, "Deprecate", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.EffectiveFromUtc.HasValue)
                    {
                        metadata.ScheduleActivation(request.EffectiveFromUtc.Value, request.EffectiveToUtc);
                    }
                    metadata.Deprecate();
                }
                else if (string.Equals(action, "Retire", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.Retire();
                }
                else if (string.Equals(action, "Schedule", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(action, "Activate", StringComparison.OrdinalIgnoreCase))
                {
                    var effectiveFrom = request.EffectiveFromUtc ?? DateTimeOffset.UtcNow;
                    metadata.ScheduleActivation(effectiveFrom, request.EffectiveToUtc);
                }
                else
                {
                    var msg = $"Unsupported LifecycleAction '{request.LifecycleAction}'. Allowed values: Schedule, Activate, Deprecate, Retire.";
                    _instrumentation.SchemaScheduleFailed(request.SchemaName, request.Version, msg);
                    return Error.Validation(ErrorCodes.SchemaInvalidSchedule, msg);
                }
            }
            else if (request.EffectiveFromUtc.HasValue)
            {
                metadata.ScheduleActivation(request.EffectiveFromUtc.Value, request.EffectiveToUtc);
            }
            else if (request.IdentifierConfig is null)
            {
                var msg = "Either EffectiveFromUtc, LifecycleAction, or IdentifierConfig must be provided.";
                _instrumentation.SchemaScheduleFailed(request.SchemaName, request.Version, msg);
                return Error.Validation(ErrorCodes.SchemaInvalidSchedule, msg);
            }
        }
        catch (Exception ex)
        {
            _instrumentation.SchemaScheduleFailed(request.SchemaName, request.Version, ex.Message);
            return Error.Validation(ErrorCodes.SchemaInvalidSchedule, ex.Message);
        }

        var updateResult = await schemaRegistry.UpdateSchemaMetadataAsync(metadata, cancellationToken);
        if (updateResult.IsFailure)
        {
            return updateResult.Error;
        }

        var stateNode = metadata.State.ToNode();
        _instrumentation.SchemaScheduleUpdated(
            metadata.Name,
            metadata.Version,
            stateNode.Name,
            metadata.EffectiveFromUtc,
            metadata.EffectiveToUtc);

        return new ScheduleSchemaResult(
            SchemaName: metadata.Name,
            Version: metadata.Version,
            State: stateNode,
            EffectiveFromUtc: metadata.EffectiveFromUtc,
            EffectiveToUtc: metadata.EffectiveToUtc,
            Sha256Hash: metadata.Sha256Hash,
            IdentifierConfig: metadata.IdentifierConfig);
    }
}
