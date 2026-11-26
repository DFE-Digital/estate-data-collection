using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.AzureFunctions.Admin.Models.Dtos;

public record ScheduleSchemaBodyDto(
    DateTimeOffset? EffectiveFromUtc = null,
    DateTimeOffset? EffectiveToUtc = null,
    string? LifecycleAction = null,
    SchemaIdentifierConfig? IdentifierConfig = null,
    SchemaValidationPolicy? ValidationPolicy = null);
