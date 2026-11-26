namespace EstateDataCollection.Application.Validators.Workflow;

public enum WorkflowStepCondition
{
    Always = 0,
    NoCriticalIssues = 1,
    NoBlockingIssues = 2,
    PreviousStepSucceeded = 3
}
