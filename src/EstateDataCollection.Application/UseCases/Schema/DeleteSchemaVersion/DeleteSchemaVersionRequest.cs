namespace EstateDataCollection.Application.UseCases.Schema.DeleteSchemaVersion;

public sealed record DeleteSchemaVersionRequest(
    string SchemaName,
    int Version
);
