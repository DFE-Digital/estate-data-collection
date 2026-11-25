namespace EstateDataCollection.Application.Validators.Workflow;

public sealed record ValidationWorkflowDefinition(
    ValidationOperationKind OperationKind,
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
        new(
            WorkflowStepIds.QualityRules,
            RunCondition: WorkflowStepCondition.NoCriticalIssues,
            DependsOnSteps: [WorkflowStepIds.JsonSchema])
    ];
}

public static class WorkflowStepIds
{
    public const string JsonSchema = "json-schema";
    public const string IdentifierExtraction = "identifier-extraction";
    public const string CrossSubmissionEnrichment = "cross-submission-enrichment";
    public const string ReferenceDataEnrichment = "reference-data-enrichment";
    public const string QualityRules = "quality-rules";
    public const string ContractConsistency = "contract-consistency-analyzer";
}
