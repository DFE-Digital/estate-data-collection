using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public sealed record ContractConsistencyReport(
    IReadOnlyList<ValidationError> Findings,
    IReadOnlyList<ValidationMessage> Errors,
    IReadOnlyList<ValidationMessage> Warnings);

public interface IContractConsistencyAnalyzer : IApplication
{
    ValidationEngineDescriptor Descriptor { get; }

    ContractConsistencyReport Analyze(
        string? rawSchemaJson,
        IReadOnlyList<BusinessRule>? rules);
}
