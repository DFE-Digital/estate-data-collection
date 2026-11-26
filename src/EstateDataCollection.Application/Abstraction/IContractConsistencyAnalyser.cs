using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Markers;

namespace EstateDataCollection.Application.Abstraction;

public interface IContractConsistencyAnalyser : IApplication
{
    WorkflowEngineDescriptor Descriptor { get; }

    ContractConsistencyReport Analyse(
        string? rawSchemaJson,
        IReadOnlyList<BusinessRule>? rules);
}
