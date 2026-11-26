namespace EstateDataCollection.Application.Validators.Workflow;

public enum WorkflowOperationKind
{
    SubmissionDraft = 0,
    SubmissionFinalise = 1,
    SchemaVerify = 2,
    SchemaIngest = 3
}
