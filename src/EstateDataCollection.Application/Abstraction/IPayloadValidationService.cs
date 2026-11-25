using System.Text.Json.Nodes;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Validators.Workflow;
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

    Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        ValidationOperationKind operationKind,
        IReadOnlyDictionary<string, JsonNode?>? ambientFixtures = null,
        CancellationToken cancellationToken = default)
    {
        return ValidatePayloadWithTraceAsync(
            rawPayloadJson,
            schema,
            operationKind == ValidationOperationKind.SubmissionDraft,
            cancellationToken);
    }
}
