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
    IValidationWorkflowRunner? workflowRunner = null
) : IPayloadValidationService
{
    private readonly ValidationOptions _options = options?.Value ?? new ValidationOptions();
    private readonly IValidationWorkflowRunner _workflowRunner =
        workflowRunner ?? ValidationWorkflowRunner.CreateDefault(schemaValidator, logicValidator, processInstrumentation);

    public IReadOnlyList<ValidationEngineDescriptor> GetRegisteredEngines()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var engines = new List<ValidationEngineDescriptor>();

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
            ? ValidationOperationKind.SubmissionDraft
            : ValidationOperationKind.SubmissionFinalise;

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
        ValidationOperationKind operationKind,
        IReadOnlyDictionary<string, JsonNode?>? ambientFixtures = null,
        CancellationToken cancellationToken = default)
    {
        var submissionId = SubmissionExecutionScope.Current?.SubmissionId is { } id && id != Guid.Empty
            ? id
            : (Guid?)null;

        var context = new ValidationWorkflowContext(
            operationKind,
            schema,
            rawPayloadJson,
            _options,
            submissionId,
            ambientFixtures);

        return _workflowRunner.RunAsync(context, cancellationToken);
    }
}
