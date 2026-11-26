using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Workflow;

public interface IWorkflowRunner : IApplication
{
    IReadOnlyList<WorkflowEngineDescriptor> GetRegisteredEngineDescriptors();

    Task<Result<PayloadValidationReport>> RunAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default);
}
