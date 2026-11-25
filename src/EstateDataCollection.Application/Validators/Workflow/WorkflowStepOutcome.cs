namespace EstateDataCollection.Application.Validators.Workflow;

public sealed record WorkflowStepOutcome(
    bool Succeeded,
    bool HaltPipeline = false,
    string? Summary = null);
