namespace EstateDataCollection.Application.UseCases.Schema.GetSchemas;

public sealed record GetSchemasRequest(
    string? SchemaName = null,
    bool IncludeUnscheduled = false,
    bool IncludeActiveVersionFields = true
);
