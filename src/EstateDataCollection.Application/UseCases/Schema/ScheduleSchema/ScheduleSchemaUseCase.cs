using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;

public sealed class ScheduleSchemaUseCase(
    ISchemaRegistry schemaRegistry,
    ScheduleSchemaUseCaseInstrumentation? instrumentation = null,
    IOptions<ValidationOptions>? validationOptions = null
) : IUseCase<ScheduleSchemaResult, ScheduleSchemaRequest>
{
    private readonly ScheduleSchemaUseCaseInstrumentation _instrumentation =
        instrumentation ?? new ScheduleSchemaUseCaseInstrumentation(NullLogger<ScheduleSchemaUseCase>.Instance);

    private readonly ValidationOptions _validationOptions =
        validationOptions?.Value ?? new ValidationOptions();

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

        if (request.ValidationPolicy is not null)
        {
            var policyValidation = request.ValidationPolicy.ValidateDefinition();
            if (policyValidation.IsFailure)
            {
                return policyValidation.Error;
            }
        }

        var action = request.LifecycleAction?.Trim();
        var isRetireOrDeprecateOnly = request.IdentifierConfig is null &&
                                      (string.Equals(action, "Retire", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(action, "Deprecate", StringComparison.OrdinalIgnoreCase) && !request.EffectiveFromUtc.HasValue);

        if (!isRetireOrDeprecateOnly)
        {
            var effectiveCandidate = request.IdentifierConfig ?? metadata.IdentifierConfig;
            var alignment = IdentifierSchemaValidator.ValidateAndAlign(
                metadata.RawJsonSchema,
                effectiveCandidate,
                _validationOptions.DefaultIdentifierJsonPaths,
                _validationOptions.RequireIdentifierOnFinalise);

            if (!alignment.IsValid)
            {
                var validationErrors = alignment.Errors
                    .Select(e => new ValidationError(
                        string.IsNullOrWhiteSpace(e.Identifier) ? "identifierConfig.jsonPointer" : e.Identifier,
                        e.Message))
                    .ToList();
                var summary = string.Join("; ", validationErrors.Select(e => e.Message));

                _instrumentation.SchemaScheduleFailed(request.SchemaName, request.Version, summary);
                return Error.Validation(
                    ErrorCodes.SchemaInvalidIdentifierConfig,
                    new { Message = summary },
                    validationErrors);
            }

            if (effectiveCandidate is not null)
            {
                metadata.ConfigureIdentifier(alignment.EffectiveConfig);
            }
        }

        if (request.ValidationPolicy is not null)
        {
            metadata.ConfigureValidationPolicy(request.ValidationPolicy);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(action))
            {
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
            else if (request.IdentifierConfig is null && request.ValidationPolicy is null)
            {
                var msg = "Either EffectiveFromUtc, LifecycleAction, IdentifierConfig, or ValidationPolicy must be provided.";
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
            IdentifierConfig: metadata.IdentifierConfig,
            ValidationPolicy: metadata.ValidationPolicy);
    }
}
