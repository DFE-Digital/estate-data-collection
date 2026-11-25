using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators.Workflow;

public sealed class ValidationWorkflowContext(
    ValidationOperationKind operationKind,
    ResolvedSchema schema,
    string rawPayloadJson,
    ValidationOptions? options = null,
    Guid? submissionId = null,
    IReadOnlyDictionary<string, JsonNode?>? ambientFixtures = null)
{
    public ValidationOperationKind OperationKind { get; } = operationKind;
    public ResolvedSchema Schema { get; } = schema;
    public string RawPayloadJson { get; } = rawPayloadJson;
    public ValidationOptions Options { get; } = options ?? new ValidationOptions();
    public SchemaValidationPolicy Policy { get; } = schema.Metadata.EffectiveValidationPolicy;
    public Guid? SubmissionId { get; } = submissionId;
    public IReadOnlyDictionary<string, JsonNode?>? AmbientFixtures { get; } = ambientFixtures;

    public bool IsDraftUpdate
    {
        get { return OperationKind == ValidationOperationKind.SubmissionDraft; }
    }

    public double EffectiveNumericTolerance
    {
        get
        {
            if (Policy.NumericToleranceOverride is { } schemaOverride)
            {
                return (double)schemaOverride;
            }

            if (Options.CurrencyRoundingTolerance is { } currencyTolerance)
            {
                return (double)currencyTolerance;
            }

            return Options.FloatArtifactEpsilon;
        }
    }

    public PayloadIdentifierExtractor.ExtractionEvaluation? IdentifierEvaluation { get; set; }

    public SubmissionIdentifier? ExtractedIdentifier
    {
        get { return IdentifierEvaluation?.Identifier; }
    }

    public Dictionary<string, JsonNode?> AmbientContext { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> Items { get; } = new(StringComparer.Ordinal);

    public List<ValidationError> AllIssues { get; } = [];

    public List<ValidationEngineTrace> EngineTraces { get; } = [];

    public Dictionary<string, WorkflowStepOutcome> StepOutcomes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasCriticalIssues
    {
        get { return AllIssues.Any(e => e.Severity == RuleSeverity.Critical); }
    }

    public bool HasBlockingIssues
    {
        get { return ValidationIssuesHelper.HasIssuesAtOrAbove(AllIssues, Policy.FailOnMinimumSeverity); }
    }

    public bool HasStepFailed(string stepId)
    {
        return StepOutcomes.TryGetValue(stepId, out var outcome) && !outcome.Succeeded;
    }

    public PayloadValidationReport BuildReport(IReadOnlyList<SubmissionProcessExecution> processesExecuted)
    {
        var groupedIssues = ValidationIssuesHelper.GroupBySeverity(AllIssues);
        var blockingIssues = ValidationIssuesHelper.GetBlockingIssues(AllIssues, Policy.FailOnMinimumSeverity);

        return new PayloadValidationReport(
            Errors: blockingIssues,
            ProcessesExecuted: processesExecuted,
            IdentifierEvaluation: IdentifierEvaluation,
            EngineTraces: EngineTraces,
            Issues: groupedIssues,
            AllIssues: AllIssues);
    }
}
