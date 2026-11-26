using EstateDataCollection.Common;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;

public sealed record ScheduleSchemaResult(
    string SchemaName,
    int Version,
    Node<int> State,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string Sha256Hash,
    SchemaIdentifierConfig? IdentifierConfig = null,
    SchemaValidationPolicy? ValidationPolicy = null
);
