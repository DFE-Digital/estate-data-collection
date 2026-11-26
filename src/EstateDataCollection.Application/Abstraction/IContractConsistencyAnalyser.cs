using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public sealed record ContractConsistencyReport(
    IReadOnlyList<ValidationError> Findings,
    IReadOnlyList<ValidationMessage> Errors,
    IReadOnlyList<ValidationMessage> Warnings);

public interface IContractConsistencyAnalyser : IApplication
{
    WorkflowEngineDescriptor Descriptor { get; }

    ContractConsistencyReport Analyse(
        string? rawSchemaJson,
        IReadOnlyList<BusinessRule>? rules);
}
