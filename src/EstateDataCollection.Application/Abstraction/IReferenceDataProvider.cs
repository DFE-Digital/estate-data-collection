using System.Text.Json.Nodes;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Abstraction;

public interface IReferenceDataProvider : IInfrastructure
{
    Task<Result<IReadOnlyDictionary<string, JsonNode?>>> GetReferenceDatasetsAsync(
        SchemaMetadata metadata,
        CancellationToken cancellationToken = default);
}
