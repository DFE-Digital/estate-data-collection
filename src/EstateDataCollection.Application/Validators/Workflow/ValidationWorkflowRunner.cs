using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators.Workflow.Steps;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Workflow;

public sealed class ValidationWorkflowRunner : IValidationWorkflowRunner
{
    private readonly Dictionary<string, IWorkflowStep> _stepsById;
    private readonly Dictionary<(ValidationOperationKind OperationKind, string PipelineKey), ValidationWorkflowDefinition> _definitions;
    private readonly ProcessExecutionInstrumentation? _processInstrumentation;
    private readonly IReadOnlyList<ValidationEngineDescriptor> _engineDescriptors;

    public ValidationWorkflowRunner(
        IEnumerable<IWorkflowStep> steps,
        IEnumerable<ValidationWorkflowDefinition>? definitions = null,
        ProcessExecutionInstrumentation? processInstrumentation = null)
    {
        _processInstrumentation = processInstrumentation;
        _stepsById = new Dictionary<string, IWorkflowStep>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            _stepsById[step.StepId] = step;
        }

        _definitions = new Dictionary<(ValidationOperationKind, string), ValidationWorkflowDefinition>();

        // Register built-in Default pipelines
        RegisterDefaultDefinitions();

        // Allow DI-registered definitions to override or extend defaults
        if (definitions is not null)
        {
            foreach (var def in definitions)
            {
                var key = string.IsNullOrWhiteSpace(def.PipelineKey)
                    ? ValidationWorkflowDefinition.DefaultPipelineKey
                    : def.PipelineKey.Trim();
                _definitions[(def.OperationKind, key)] = def;
            }
        }

        _engineDescriptors = BuildEngineDescriptors(_stepsById.Values);
    }

    public static ValidationWorkflowRunner CreateDefault(
        ISchemaValidator schemaValidator,
        IBusinessLogicValidator logicValidator,
        ProcessExecutionInstrumentation? processInstrumentation = null)
    {
        IWorkflowStep[] steps =
        [
            new JsonSchemaWorkflowStep(schemaValidator),
            new IdentifierExtractionWorkflowStep(),
            new QualityRulesWorkflowStep(logicValidator)
        ];

        return new ValidationWorkflowRunner(steps, definitions: null, processInstrumentation);
    }

    public IReadOnlyList<ValidationEngineDescriptor> GetRegisteredEngineDescriptors()
    {
        return _engineDescriptors;
    }

    public async Task<Result<PayloadValidationReport>> RunAsync(
        ValidationWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var pipelineKey = string.IsNullOrWhiteSpace(context.Policy.WorkflowPipelineKey)
            ? ValidationWorkflowDefinition.DefaultPipelineKey
            : context.Policy.WorkflowPipelineKey.Trim();

        if (!_definitions.TryGetValue((context.OperationKind, pipelineKey), out var definition))
        {
            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed.GetName(),
                $"Workflow pipeline '{pipelineKey}' is not registered for operation '{context.OperationKind}'.");
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
                    $"Required workflow step '{binding.StepId}' is not registered.");
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
        _definitions[(ValidationOperationKind.SubmissionDraft, ValidationWorkflowDefinition.DefaultPipelineKey)] =
            new ValidationWorkflowDefinition(
                ValidationOperationKind.SubmissionDraft,
                ValidationWorkflowDefinition.DefaultPipelineKey,
                ValidationWorkflowDefinition.DefaultSubmissionSteps);

        _definitions[(ValidationOperationKind.SubmissionFinalise, ValidationWorkflowDefinition.DefaultPipelineKey)] =
            new ValidationWorkflowDefinition(
                ValidationOperationKind.SubmissionFinalise,
                ValidationWorkflowDefinition.DefaultPipelineKey,
                ValidationWorkflowDefinition.DefaultSubmissionSteps);

        _definitions[(ValidationOperationKind.SchemaVerify, ValidationWorkflowDefinition.DefaultPipelineKey)] =
            new ValidationWorkflowDefinition(
                ValidationOperationKind.SchemaVerify,
                ValidationWorkflowDefinition.DefaultPipelineKey,
                ValidationWorkflowDefinition.DefaultSubmissionSteps);

        _definitions[(ValidationOperationKind.SchemaIngest, ValidationWorkflowDefinition.DefaultPipelineKey)] =
            new ValidationWorkflowDefinition(
                ValidationOperationKind.SchemaIngest,
                ValidationWorkflowDefinition.DefaultPipelineKey,
                ValidationWorkflowDefinition.DefaultSubmissionSteps);
    }

    private static bool ShouldRunBinding(
        WorkflowStepBinding binding,
        ValidationWorkflowContext context,
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
                        ? "Skipped because structural JSON Schema validation failed"
                        : $"Skipped because prerequisite step '{depStepId}' did not succeed";
                    return false;
                }
            }
        }

        switch (binding.RunCondition)
        {
            case WorkflowStepCondition.NoCriticalIssues when context.HasCriticalIssues:
                skipReason = "Skipped because an earlier step emitted a Critical issue";
                return false;
            case WorkflowStepCondition.NoBlockingIssues when context.HasBlockingIssues:
                skipReason = "Skipped because an earlier step emitted blocking validation issues";
                return false;
            case WorkflowStepCondition.PreviousStepSucceeded when !previousStepSucceeded:
                skipReason = "Skipped because the previous workflow step did not succeed";
                return false;
            default:
                skipReason = null;
                return true;
        }
    }

    private static IReadOnlyList<ValidationEngineDescriptor> BuildEngineDescriptors(IEnumerable<IWorkflowStep> steps)
    {
        var list = new List<ValidationEngineDescriptor>();
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
