using EstateDataCollection.Common;

namespace EstateDataCollection.Application.UseCases.Schema.SignOffSchema;

public sealed record SignOffSchemaResult(
    string SchemaName,
    int Version,
    Node<int> State,
    string SignedOffBy,
    DateTimeOffset SignedOffAtUtc
);
