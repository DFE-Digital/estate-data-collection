using System.Diagnostics;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators.Workflow.Steps;

public sealed class ReferenceDataEnrichmentWorkflowStep(
    IReferenceDataProvider? referenceDataProvider = null
) : IWorkflowStep
{
    public string StepId
    {
        get { return WorkflowStepIds.ReferenceDataEnrichment; }
    }

    public SubmissionProcessDescriptor ProcessDescriptor
    {
        get { return SubmissionProcessCatalog.ReferenceDataEnrichment; }
    }

    public ValidationEngineDescriptor EngineDescriptor
    {
        get
        {
            return new ValidationEngineDescriptor(
                WorkflowStepIds.ReferenceDataEnrichment,
                "Reference Data Enrichment",
                ValidationEngineKinds.ContextEnrichment,
                "Loads versioned reference datasets from blob storage, local paths, or verify fixtures into ambient.reference.");
        }
    }

    public async Task<Result<WorkflowStepOutcome>> ExecuteAsync(
        ValidationWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var hasFixtures = TryExtractFixtureReferenceData(context.AmbientFixtures, out var fixtureDatasets);
        if (referenceDataProvider is null && !hasFixtures)
        {
            RecordSkipped(context, "Reference data provider is not configured");
            return new WorkflowStepOutcome(Succeeded: true, Summary: "Skipped (no reference data provider)");
        }

        var swStart = Stopwatch.GetTimestamp();

        var stepResult = await SubmissionExecutionScope.StepAsync(
            ProcessDescriptor,
            async () =>
            {
                var merged = new JsonObject();

                if (referenceDataProvider is not null)
                {
                    var loaded = await referenceDataProvider.GetReferenceDatasetsAsync(context.Schema.Metadata, cancellationToken);
                    if (loaded.IsFailure)
                    {
                        return Result<int>.Failure(loaded.Error);
                    }

                    foreach (var (key, value) in loaded.Value)
                    {
                        merged[key] = value?.DeepClone();
                    }
                }

                if (fixtureDatasets is not null)
                {
                    foreach (var (key, value) in fixtureDatasets)
                    {
                        merged[key] = value?.DeepClone();
                        var underscoreKey = key.Replace('-', '_');
                        if (!string.Equals(underscoreKey, key, StringComparison.OrdinalIgnoreCase))
                        {
                            merged[underscoreKey] = value?.DeepClone();
                        }
                    }
                }

                InjectReferenceAmbient(context, merged);
                return Result<int>.Success(merged.Count);
            },
            count => new ProcessStepDetails(
                Outcome: SubmissionProcessOutcome.Succeeded,
                ItemCount: count,
                Summary: count == 0
                    ? "No reference datasets registered for schema"
                    : $"Loaded {count} reference dataset key(s) into ambient.reference"),
            err => new ProcessStepDetails(
                Outcome: SubmissionProcessOutcome.Failed,
                Summary: err.Description ?? err.Code,
                ErrorCode: err.Code));

        var elapsedMs = (long)Stopwatch.GetElapsedTime(swStart).TotalMilliseconds;

        if (stepResult.IsFailure)
        {
            context.EngineTraces.Add(new ValidationEngineTrace(
                Descriptor: EngineDescriptor,
                Outcome: nameof(SubmissionProcessOutcome.Failed),
                ItemsEvaluated: 0,
                ViolationCount: 1,
                DurationMs: elapsedMs,
                Summary: stepResult.Error.Description ?? stepResult.Error.Code));
            return stepResult.Error;
        }

        var count = stepResult.Value;
        context.EngineTraces.Add(new ValidationEngineTrace(
            Descriptor: EngineDescriptor,
            Outcome: nameof(SubmissionProcessOutcome.Succeeded),
            ItemsEvaluated: count,
            ViolationCount: 0,
            DurationMs: elapsedMs,
            Summary: count == 0
                ? "No reference datasets registered for schema"
                : $"Loaded {count} reference dataset key(s) into ambient.reference"));

        return new WorkflowStepOutcome(Succeeded: true, Summary: $"Loaded {count} reference dataset key(s)");
    }

    public void RecordSkipped(ValidationWorkflowContext context, string reason)
    {
        SubmissionExecutionScope.Current?.RecordSkipped(
            ProcessDescriptor,
            itemCount: 0,
            summary: reason);

        context.EngineTraces.Add(new ValidationEngineTrace(
            Descriptor: EngineDescriptor,
            Outcome: nameof(SubmissionProcessOutcome.Skipped),
            ItemsEvaluated: 0,
            ViolationCount: 0,
            DurationMs: 0,
            Summary: reason));
    }

    private static bool TryExtractFixtureReferenceData(
        IReadOnlyDictionary<string, JsonNode?>? fixtures,
        out JsonObject? refObj)
    {
        refObj = null;
        if (fixtures is null)
        {
            return false;
        }

        if (fixtures.TryGetValue("reference", out var direct) && direct is JsonObject directObj)
        {
            refObj = directObj.DeepClone().AsObject();
            return true;
        }

        if (fixtures.TryGetValue("ambient", out var ambientNode) &&
            ambientNode is JsonObject ambientObj &&
            ambientObj.TryGetPropertyValue("reference", out var nested) &&
            nested is JsonObject nestedObj)
        {
            refObj = nestedObj.DeepClone().AsObject();
            return true;
        }

        return false;
    }

    private static void InjectReferenceAmbient(ValidationWorkflowContext context, JsonObject refObj)
    {
        context.AmbientContext["reference"] = refObj.DeepClone();

        var ambientRoot = context.AmbientContext.TryGetValue("ambient", out var existingAmbient) && existingAmbient is JsonObject existingObj
            ? existingObj
            : new JsonObject();
        ambientRoot["reference"] = refObj;
        context.AmbientContext["ambient"] = ambientRoot;
    }
}
