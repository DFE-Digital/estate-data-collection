using EstateDataCollection.Common;

namespace EstateDataCollection.Application.UseCases.Schema.SubmitSchema;

public sealed record SubmitSchemaResult(
    string SchemaName,
    int Version,
    Node<int> State,
    string Sha256Hash,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc
);