using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators.Workflow;

public interface IWorkflowStep
{
    string StepId { get; }
    SubmissionProcessDescriptor ProcessDescriptor { get; }
    WorkflowEngineDescriptor EngineDescriptor { get; }

    Task<Result<WorkflowStepOutcome>> ExecuteAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default);

    void RecordSkipped(WorkflowContext context, string reason) { }
}
