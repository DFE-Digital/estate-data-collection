using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators.Workflow;

public interface IWorkflowStep
{
    string StepId { get; }
    SubmissionProcessDescriptor ProcessDescriptor { get; }
    ValidationEngineDescriptor EngineDescriptor { get; }

    Task<Result<WorkflowStepOutcome>> ExecuteAsync(
        ValidationWorkflowContext context,
        CancellationToken cancellationToken = default);

    void RecordSkipped(ValidationWorkflowContext context, string reason) { }
}
