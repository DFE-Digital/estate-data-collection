using Json.Schema;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Contracts;

public sealed record ResolvedSchema(
    SchemaMetadata Metadata,
    JsonSchema Schema
);