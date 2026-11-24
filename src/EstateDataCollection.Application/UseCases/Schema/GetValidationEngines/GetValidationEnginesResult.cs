using EstateDataCollection.Application.Abstraction;

namespace EstateDataCollection.Application.UseCases.Schema.GetValidationEngines;

public sealed record GetValidationEnginesResult(
    IReadOnlyList<ValidationEngineDescriptor> Engines);
