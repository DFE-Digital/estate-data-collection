using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public interface IPayloadValidationService : IApplication
{
    IReadOnlyList<ValidationEngineDescriptor> GetRegisteredEngines() => [];

    Task<Result<IReadOnlyList<ValidationError>>> ValidatePayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default);

    async Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var result = await ValidatePayloadAsync(rawPayloadJson, schema, isDraftUpdate, cancellationToken);
        return result.IsFailure
            ? result.Error
            : new PayloadValidationReport(result.Value, []);
    }
}
