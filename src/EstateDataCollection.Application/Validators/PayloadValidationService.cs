using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators.Workflow;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.Validators;

public sealed class PayloadValidationService(
    ISchemaValidator schemaValidator,
    IBusinessLogicValidator logicValidator,
    IOptions<ValidationOptions>? options = null,
    ProcessExecutionInstrumentation? processInstrumentation = null,
    IWorkflowRunner? workflowRunner = null,
    IContractConsistencyAnalyzer? consistencyAnalyzer = null
) : IPayloadValidationService
{
    private readonly ValidationOptions _options = options?.Value ?? new ValidationOptions();
    private readonly IWorkflowRunner _workflowRunner =
        workflowRunner ?? WorkflowRunner.CreateDefault(schemaValidator, logicValidator, processInstrumentation);
    private readonly WorkflowEngineDescriptor _consistencyDescriptor =
        consistencyAnalyzer?.Descriptor ?? ContractConsistencyAnalyzer.EngineDescriptor;

    public IReadOnlyList<WorkflowEngineDescriptor> GetRegisteredEngines()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var engines = new List<WorkflowEngineDescriptor>();

        foreach (var stepDescriptor in _workflowRunner.GetRegisteredEngineDescriptors())
        {
            if (seen.Add(stepDescriptor.EngineId))
            {
                engines.Add(stepDescriptor);
            }
        }

        var ruleEngines = logicValidator.GetRegisteredEngines();
        if (ruleEngines is { Count: > 0 })
        {
            foreach (var ruleEngine in ruleEngines)
            {
                if (seen.Add(ruleEngine.EngineId))
                {
                    engines.Add(ruleEngine);
                }
            }
        }

        if (seen.Add(_consistencyDescriptor.EngineId))
        {
            engines.Add(_consistencyDescriptor);
        }

        return engines;
    }

    public async Task<Result<IReadOnlyList<ValidationError>>> ValidatePayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var report = await ValidatePayloadWithTraceAsync(rawPayloadJson, schema, isDraftUpdate, cancellationToken);
        return report.IsFailure ? report.Error : Result<IReadOnlyList<ValidationError>>.Success(report.Value.Errors);
    }

    public Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var operationKind = isDraftUpdate
            ? WorkflowOperationKind.SubmissionDraft
            : WorkflowOperationKind.SubmissionFinalise;

        return ValidatePayloadWithTraceAsync(
            rawPayloadJson,
            schema,
            operationKind,
            ambientFixtures: null,
            cancellationToken);
    }

    public Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        WorkflowOperationKind operationKind,
        IReadOnlyDictionary<string, JsonNode?>? ambientFixtures = null,
        CancellationToken cancellationToken = default)
    {
        var submissionId = SubmissionExecutionScope.Current?.SubmissionId is { } id && id != Guid.Empty
            ? id
            : (Guid?)null;

        var context = new WorkflowContext(
            operationKind,
            schema,
            rawPayloadJson,
            _options,
            submissionId,
            ambientFixtures);

        return _workflowRunner.RunAsync(context, cancellationToken);
    }
}
