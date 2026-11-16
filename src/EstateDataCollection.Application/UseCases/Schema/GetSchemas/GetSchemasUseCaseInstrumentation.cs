using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Schema.GetSchemas;

public sealed partial class GetSchemasUseCaseInstrumentation(
    ILogger<GetSchemasUseCase> logger) : IInstrumentation
{
    [LoggerMessage(
        EventId = 1800,
        Level = LogLevel.Information,
        Message = "Listing schema catalogue (SchemaNameFilter={SchemaNameFilter}, IncludeUnscheduled={IncludeUnscheduled})")]
    public partial void ListingSchemaCatalogue(string? schemaNameFilter, bool includeUnscheduled);

    [LoggerMessage(
        EventId = 1801,
        Level = LogLevel.Information,
        Message = "Returned {SchemaCount} schema catalogue entries")]
    public partial void SchemaCatalogueReturned(int schemaCount);
}
