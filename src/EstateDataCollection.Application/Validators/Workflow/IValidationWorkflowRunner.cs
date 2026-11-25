using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Workflow;

public interface IValidationWorkflowRunner : IApplication
{
    IReadOnlyList<ValidationEngineDescriptor> GetRegisteredEngineDescriptors();

    Task<Result<PayloadValidationReport>> RunAsync(
        ValidationWorkflowContext context,
        CancellationToken cancellationToken = default);
}
