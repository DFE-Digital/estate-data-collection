using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;

/// <summary>
/// Request to assign or update effective dates (EffectiveFromUtc / EffectiveToUtc), lifecycle state,
/// or per-schema-version identifier configuration (e.g. URN JSON Pointer) for an already-ingested schema version via the Internal Admin API.
/// </summary>
public sealed record ScheduleSchemaRequest(
    string SchemaName,
    int Version,
    DateTimeOffset? EffectiveFromUtc = null,
    DateTimeOffset? EffectiveToUtc = null,
    string? LifecycleAction = null,
    SchemaIdentifierConfig? IdentifierConfig = null
);
