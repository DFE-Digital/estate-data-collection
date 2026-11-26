using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators.Workflow.Steps;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Workflow;

public sealed class WorkflowRunner : IWorkflowRunner
{
    private readonly Dictionary<string, IWorkflowStep> _stepsById;
    private readonly Dictionary<(WorkflowOperationKind OperationKind, string PipelineKey), WorkflowDefinition> _definitions;
    private readonly ProcessExecutionInstrumentation? _processInstrumentation;
    private readonly IReadOnlyList<WorkflowEngineDescriptor> _engineDescriptors;

    public WorkflowRunner(
        IEnumerable<IWorkflowStep> steps,
        IEnumerable<WorkflowDefinition>? definitions = null,
        ProcessExecutionInstrumentation? processInstrumentation = null)
    {
        _processInstrumentation = processInstrumentation;
        _stepsById = new Dictionary<string, IWorkflowStep>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            _stepsById[step.StepId] = step;
        }

        _definitions = new Dictionary<(WorkflowOperationKind, string), WorkflowDefinition>();

        // Register built-in Default pipelines
        RegisterDefaultDefinitions();

        // Allow DI-registered definitions to override or extend defaults
        if (definitions is not null)
        {
            foreach (var def in definitions)
            {
                var key = string.IsNullOrWhiteSpace(def.PipelineKey)
                    ? WorkflowDefinition.DefaultPipelineKey
                    : def.PipelineKey.Trim();
                _definitions[(def.OperationKind, key)] = def;
            }
        }

        _engineDescriptors = BuildEngineDescriptors(_stepsById.Values);
    }

    public static WorkflowRunner CreateDefault(
        ISchemaValidator schemaValidator,
        IBusinessLogicValidator logicValidator,
        ProcessExecutionInstrumentation? processInstrumentation = null,
        IIdentifierIndexRepository? identifierIndexRepository = null,
        IReferenceDataProvider? referenceDataProvider = null)
    {
        var steps = new List<IWorkflowStep>
        {
            new JsonSchemaWorkflowStep(schemaValidator),
            new IdentifierExtractionWorkflowStep()
        };

        if (identifierIndexRepository is not null)
        {
            steps.Add(new CrossSubmissionEnrichmentWorkflowStep(identifierIndexRepository));
        }

        if (referenceDataProvider is not null)
        {
            steps.Add(new ReferenceDataEnrichmentWorkflowStep(referenceDataProvider));
        }

        steps.Add(new QualityRulesWorkflowStep(logicValidator));

        return new WorkflowRunner(steps, definitions: null, processInstrumentation);
    }

    public IReadOnlyList<WorkflowEngineDescriptor> GetRegisteredEngineDescriptors()
    {
        return _engineDescriptors;
    }

    public async Task<Result<PayloadValidationReport>> RunAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var pipelineKey = string.IsNullOrWhiteSpace(context.Policy.WorkflowPipelineKey)
            ? WorkflowDefinition.DefaultPipelineKey
            : context.Policy.WorkflowPipelineKey.Trim();

        if (!_definitions.TryGetValue((context.OperationKind, pipelineKey), out var definition))
        {
            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed.GetName(),
                WorkflowMessages.WorkflowPipelineNotRegistered(pipelineKey, context.OperationKind));
        }

        var ambientScope = SubmissionExecutionScope.Current;
        using var ownedScope = ambientScope is null
            ? SubmissionExecutionScope.Start(
                context.IsDraftUpdate ? "ValidateDraftPayload" : "ValidatePayload",
                submissionId: context.SubmissionId ?? Guid.Empty,
                instrumentation: _processInstrumentation,
                initialCapacity: definition.Steps.Count)
            : null;
        var scope = ambientScope ?? ownedScope!;

        var previousStepSucceeded = true;

        foreach (var binding in definition.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!binding.Enabled)
            {
                continue;
            }

            if (!_stepsById.TryGetValue(binding.StepId, out var step))
            {
                if (!binding.Required)
                {
                    continue;
                }

                return Error.Failure(
                    ErrorCodes.SchemaVerificationFailed.GetName(),
                    WorkflowMessages.RequiredWorkflowStepNotRegistered(binding.StepId));
            }

            if (!ShouldRunBinding(binding, context, previousStepSucceeded, out var skipReason))
            {
                step.RecordSkipped(context, skipReason!);
                context.StepOutcomes[step.StepId] = new WorkflowStepOutcome(Succeeded: false, Summary: skipReason);
                continue;
            }

            var stepResult = await step.ExecuteAsync(context, cancellationToken);
            if (stepResult.IsFailure)
            {
                return stepResult.Error;
            }

            var outcome = stepResult.Value;
            context.StepOutcomes[step.StepId] = outcome;
            previousStepSucceeded = outcome.Succeeded;

            if (outcome.HaltPipeline)
            {
                break;
            }
        }

        return context.BuildReport(scope.Processes.ToArray());
    }

    private void RegisterDefaultDefinitions()
    {
        _definitions[(WorkflowOperationKind.SubmissionDraft, WorkflowDefinition.DefaultPipelineKey)] =
            new WorkflowDefinition(
                WorkflowOperationKind.SubmissionDraft,
                WorkflowDefinition.DefaultPipelineKey,
                WorkflowDefinition.DefaultSubmissionSteps);

        _definitions[(WorkflowOperationKind.SubmissionFinalise, WorkflowDefinition.DefaultPipelineKey)] =
            new WorkflowDefinition(
                WorkflowOperationKind.SubmissionFinalise,
                WorkflowDefinition.DefaultPipelineKey,
                WorkflowDefinition.DefaultSubmissionSteps);

        _definitions[(WorkflowOperationKind.SchemaVerify, WorkflowDefinition.DefaultPipelineKey)] =
            new WorkflowDefinition(
                WorkflowOperationKind.SchemaVerify,
                WorkflowDefinition.DefaultPipelineKey,
                WorkflowDefinition.DefaultSubmissionSteps);

        _definitions[(WorkflowOperationKind.SchemaIngest, WorkflowDefinition.DefaultPipelineKey)] =
            new WorkflowDefinition(
                WorkflowOperationKind.SchemaIngest,
                WorkflowDefinition.DefaultPipelineKey,
                WorkflowDefinition.DefaultSubmissionSteps);
    }

    private static bool ShouldRunBinding(
        WorkflowStepBinding binding,
        WorkflowContext context,
        bool previousStepSucceeded,
        out string? skipReason)
    {
        if (binding.DependsOnSteps is { Count: > 0 })
        {
            foreach (var depStepId in binding.DependsOnSteps)
            {
                if (context.HasStepFailed(depStepId))
                {
                    skipReason = string.Equals(depStepId, WorkflowStepIds.JsonSchema, StringComparison.OrdinalIgnoreCase)
                        ? WorkflowMessages.SkippedStructuralSchemaFailed
                        : WorkflowMessages.SkippedPrerequisiteStepFailed(depStepId);
                    return false;
                }
            }
        }

        switch (binding.RunCondition)
        {
            case WorkflowStepCondition.NoCriticalIssues when context.HasCriticalIssues:
                skipReason = WorkflowMessages.SkippedCriticalIssue;
                return false;
            case WorkflowStepCondition.NoBlockingIssues when context.HasBlockingIssues:
                skipReason = WorkflowMessages.SkippedBlockingIssues;
                return false;
            case WorkflowStepCondition.PreviousStepSucceeded when !previousStepSucceeded:
                skipReason = WorkflowMessages.SkippedPreviousStepFailed;
                return false;
            default:
                skipReason = null;
                return true;
        }
    }

    private static IReadOnlyList<WorkflowEngineDescriptor> BuildEngineDescriptors(IEnumerable<IWorkflowStep> steps)
    {
        var list = new List<WorkflowEngineDescriptor>();
        foreach (var step in steps)
        {
            if (string.Equals(step.StepId, WorkflowStepIds.QualityRules, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            list.Add(step.EngineDescriptor);
        }
        return list;
    }
}
