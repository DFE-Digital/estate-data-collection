namespace EstateDataCollection.Application.Validators.Workflow;

public sealed record WorkflowStepBinding(
    string StepId,
    WorkflowStepCondition RunCondition = WorkflowStepCondition.Always,
    bool Enabled = true,
    bool Required = true,
    IReadOnlyList<string>? DependsOnSteps = null);
