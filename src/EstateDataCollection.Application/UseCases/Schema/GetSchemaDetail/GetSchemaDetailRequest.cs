namespace EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;

public sealed record GetSchemaDetailRequest(
    string SchemaName,
    int Version,
    bool IncludeUnscheduled = false,
    bool IncludeRawJsonSchema = false,
    bool IncludeUpcoming = false,
    bool IncludeRetired = false
);
