namespace EstateDataCollection.Application.Validators.Workflow;

public sealed record WorkflowDefinition(
    WorkflowOperationKind OperationKind,
    string PipelineKey,
    IReadOnlyList<WorkflowStepBinding> Steps)
{
    public const string DefaultPipelineKey = "Default";

    public static IReadOnlyList<WorkflowStepBinding> DefaultSubmissionSteps { get; } =
    [
        new(WorkflowStepIds.JsonSchema),
        new(WorkflowStepIds.IdentifierExtraction),
        new(WorkflowStepIds.CrossSubmissionEnrichment, Required: false),
        new(WorkflowStepIds.ReferenceDataEnrichment, Required: false),
        new(WorkflowStepIds.QualityRules,
            RunCondition: WorkflowStepCondition.NoCriticalIssues,
            DependsOnSteps: [WorkflowStepIds.JsonSchema])
    ];
}