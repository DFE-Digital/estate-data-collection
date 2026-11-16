using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public interface IPayloadValidationService : IApplication
{
    Task<Result<IReadOnlyList<ValidationError>>> ValidatePayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default);
}
