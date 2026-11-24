using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;

public sealed partial class GetSchemaDetailUseCaseInstrumentation(
    ILogger<GetSchemaDetailUseCase> logger) : IInstrumentation
{
    [LoggerMessage(
        EventId = 1810,
        Level = LogLevel.Information,
        Message = "Fetching schema detail for {SchemaName} v{Version} (IncludeUnscheduled={IncludeUnscheduled})")]
    public partial void FetchingSchemaDetail(string schemaName, int version, bool includeUnscheduled);
}
