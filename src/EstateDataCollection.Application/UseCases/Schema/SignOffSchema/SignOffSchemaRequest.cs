namespace EstateDataCollection.Application.UseCases.Schema.SignOffSchema;

public sealed record SignOffSchemaRequest(
    string SchemaName,
    int Version,
    string SignedOffBy
);
