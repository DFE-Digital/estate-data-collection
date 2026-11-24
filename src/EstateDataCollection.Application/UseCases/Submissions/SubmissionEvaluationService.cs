using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.UseCases.Submissions;

public sealed class SubmissionEvaluationService(
    ISubmissionInfrastructure infrastructure,
    ISchemaRegistry registry,
    IPayloadValidationService validationService,
    IOptions<ValidationOptions>? validationOptions = null
) : ISubmissionEvaluationService
{
    private readonly ValidationOptions _options = validationOptions?.Value ?? new ValidationOptions();

    public async Task<SubmissionDraftInventory> LoadDraftInventoryAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        async Task<Result<IReadOnlyList<SubmissionDraftItem>>> FetchDraftItems()
        {
            var raw = await infrastructure.GetDraftItemsAsync(submissionId, cancellationToken);
            return raw ?? Result<IReadOnlyList<SubmissionDraftItem>>.Success([]);
        }

        var draftItemsResult = await SubmissionExecutionScope.StepAsync(
            SubmissionProcessCatalog.GetDraftItems,
            FetchDraftItems,
            items => new ProcessStepDetails(
                ItemCount: items.Count,
                Summary: items.Count == 0
                    ? "No timestamped draft chunks listed"
                    : $"Loaded {items.Count} draft chunk(s)"),
            err => new ProcessStepDetails(
                SubmissionProcessOutcome.Missing,
                ItemCount: 0,
                Summary: err.Description ?? err.Code));

        var items = draftItemsResult is { IsSuccess: true, Value: not null }
            ? draftItemsResult.Value
            : [];

        DateTimeOffset? lastDraftAtUtc = items.Count > 0
            ? items.Max(d => d.CreatedAtUtc)
            : null;

        return new SubmissionDraftInventory(items, items.Count, lastDraftAtUtc);
    }

    public Task<Result<string>> AssembleStoredDraftAsync(
        Guid submissionId,
        SubmissionDraftInventory inventory,
        CancellationToken cancellationToken = default)
    {
        if (inventory.Count == 1)
        {
            return Task.FromResult(Result<string>.Success(inventory.Items[0].PayloadJson));
        }

        if (inventory.Count > 1)
        {
            return Task.FromResult(SubmissionExecutionScope.StepResultSync(
                SubmissionProcessCatalog.DraftPayloadMerger,
                () => DraftPayloadMerger.Merge(inventory.Items),
                _ => new ProcessStepDetails(
                    ItemCount: inventory.Count,
                    Summary: $"Merged {inventory.Count} timestamped draft submission(s)"),
                err => new ProcessStepDetails(
                    SubmissionProcessOutcome.Failed,
                    ItemCount: inventory.Count,
                    Summary: err.Description ?? err.Code)));
        }

        return SubmissionExecutionScope.StepAsync(
            SubmissionProcessCatalog.GetDraftPayload,
            () => infrastructure.GetDraftPayloadAsync(submissionId, cancellationToken),
            payload => new ProcessStepDetails(
                ItemCount: Math.Max(1, inventory.Count),
                Summary: $"Loaded {payload.Length} char draft JSON"),
            err => IsMissingDraftError(err)
                ? new ProcessStepDetails(SubmissionProcessOutcome.Missing, Summary: "No draft payload uploaded yet")
                : new ProcessStepDetails(SubmissionProcessOutcome.Failed, Summary: err.Description ?? err.Code));
    }

    public async Task<string?> TryAssembleDraftForDetailAsync(
        SubmissionSession session,
        SubmissionDraftInventory inventory,
        CancellationToken cancellationToken = default)
    {
        if (inventory.Count > 0)
        {
            var merged = DraftPayloadMerger.Merge(inventory.Items);
            return merged.IsSuccess ? merged.Value : inventory.Items[^1].PayloadJson;
        }

        if (!session.IsFinalised)
        {
            var activeDraft = await infrastructure.GetDraftPayloadAsync(session.SubmissionId, cancellationToken);
            if (activeDraft.IsSuccess)
            {
                return activeDraft.Value;
            }
        }

        return null;
    }

    public Result<SubmissionCandidateDraft> AssembleCandidateDraft(
        SubmissionDraftInventory existingDrafts,
        string incomingPayload)
    {
        var draftCountAfterSave = existingDrafts.Count + 1;
        if (existingDrafts.Count == 0)
        {
            return new SubmissionCandidateDraft(incomingPayload, draftCountAfterSave);
        }

        var payloadsToMerge = existingDrafts.Items
            .Select(i => i.PayloadJson)
            .Append(incomingPayload)
            .ToList();

        var mergeResult = SubmissionExecutionScope.StepResultSync(
            SubmissionProcessCatalog.DraftPayloadMerger,
            () => DraftPayloadMerger.MergeJsonPayloads(payloadsToMerge),
            _ => new ProcessStepDetails(
                ItemCount: payloadsToMerge.Count,
                Summary: $"Merged incoming patch with {existingDrafts.Count} prior draft chunk(s)"),
            err => new ProcessStepDetails(
                SubmissionProcessOutcome.Failed,
                ItemCount: payloadsToMerge.Count,
                Summary: err.Description ?? err.Code));

        return mergeResult.IsFailure
            ? mergeResult.Error
            : new SubmissionCandidateDraft(mergeResult.Value, draftCountAfterSave);
    }

    public Task<Result<ResolvedSchema>> ResolveSchemaAsync(
        string schemaName,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        return SubmissionExecutionScope.StepAsync(
            SubmissionProcessCatalog.SchemaResolution,
            () => registry.GetExplicitSchemaAsync(schemaName, schemaVersion, cancellationToken),
            resolved => new ProcessStepDetails(Summary: $"{resolved.Metadata.Name} v{resolved.Metadata.Version}"));
    }

    public async Task<Result<PayloadEvaluationResult>> EvaluatePayloadAsync(
        string rawPayloadJson,
        SubmissionSession session,
        bool isDraftUpdate,
        CancellationToken cancellationToken = default)
    {
        var schemaResult = await ResolveSchemaAsync(session.SchemaName, session.SchemaVersion, cancellationToken);
        if (schemaResult.IsFailure)
        {
            return schemaResult.Error;
        }

        return await EvaluateResolvedPayloadAsync(
            rawPayloadJson,
            schemaResult.Value,
            session,
            isDraftUpdate,
            cancellationToken);
    }

    public async Task<Result<PayloadEvaluationResult>> EvaluateResolvedPayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        SubmissionSession session,
        bool isDraftUpdate,
        CancellationToken cancellationToken = default)
    {
        var ambientScope = SubmissionExecutionScope.Current;
        var countBefore = ambientScope?.Processes.Count ?? 0;

        var validationResult = await validationService.ValidatePayloadWithTraceAsync(
            rawPayloadJson,
            schema,
            isDraftUpdate,
            cancellationToken);

        if (validationResult.IsFailure)
        {
            return validationResult.Error;
        }

        // Support mocked IPayloadValidationService instances that return pre-built traces
        // rather than recording into the ambient SubmissionExecutionScope.Current.
        if (ambientScope is not null &&
            ambientScope.Processes.Count == countBefore &&
            validationResult.Value.ProcessesExecuted.Count > 0)
        {
            ambientScope.ImportProcesses(validationResult.Value.ProcessesExecuted);
        }

        var report = validationResult.Value;
        var shouldCheckIdentifier = !isDraftUpdate || _options.ValidateIdentifierOnDraftUpdate;
        var identifierEval = report.IdentifierEvaluation ?? EvaluateIdentifier(rawPayloadJson, schema, shouldCheckIdentifier);
        var auditIdentifier = BuildAuditIdentifier(session, identifierEval.Identifier);

        return new PayloadEvaluationResult(
            Schema: schema,
            ValidationErrors: report.Errors,
            IdentifierEvaluation: identifierEval,
            AuditIdentifier: auditIdentifier);
    }

    public bool IsMissingDraftError(Error error)
    {
        return string.Equals(error.Code, ErrorCodes.SubmissionNotFound.GetName(), StringComparison.Ordinal) ||
               string.Equals(error.Code, ErrorCodes.PayloadNotFound.GetName(), StringComparison.Ordinal) ||
               string.Equals(error.Code, ErrorCodes.PayloadEmpty.GetName(), StringComparison.Ordinal);
    }

    public SubmissionAuditIdentifier? BuildAuditIdentifier(
        SubmissionSession session,
        SubmissionIdentifier? extractedIdentifier = null)
    {
        var type = extractedIdentifier?.Type ?? session.IdentifierType;
        var value = extractedIdentifier?.Value ?? session.IdentifierValue;

        return type is null && value is null
            ? null
            : new SubmissionAuditIdentifier(type, value);
    }

    private PayloadIdentifierExtractor.ExtractionEvaluation EvaluateIdentifier(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool recordScopeStep)
    {
        if (SubmissionExecutionScope.Current is not null && recordScopeStep)
        {
            return SubmissionExecutionScope.StepSync(
                SubmissionProcessCatalog.IdentifierExtractor,
                () => PayloadIdentifierExtractor.Evaluate(rawPayloadJson, schema, _options),
                eval => !eval.IsValid && eval.ValidationError is not null
                    ? new ProcessStepDetails(
                        SubmissionProcessOutcome.ValidationErrorsFound,
                        Summary: eval.ValidationError.Message,
                        ErrorCode: eval.FailureError?.Code,
                        Errors:
                        [
                            new SubmissionAuditValidationIssue(
                                eval.ValidationError.PropertyPath,
                                eval.ValidationError.Message,
                                eval.ValidationError.RuleId ?? eval.FailureError?.Code)
                        ])
                    : new ProcessStepDetails(
                        Summary: eval.Identifier is { } id ? $"{id.Type}={id.Value}" : "Not required by schema"));
        }

        return PayloadIdentifierExtractor.Evaluate(rawPayloadJson, schema, _options);
    }
}
