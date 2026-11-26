using System.Diagnostics;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators.Workflow.Steps;

public sealed class CrossSubmissionEnrichmentWorkflowStep(
    IIdentifierIndexRepository? indexRepository = null
) : IWorkflowStep
{
    public string StepId => WorkflowStepIds.CrossSubmissionEnrichment;

    public SubmissionProcessDescriptor ProcessDescriptor => SubmissionProcessCatalog.CrossSubmissionEnrichment;

    public WorkflowEngineDescriptor EngineDescriptor =>
        new(
            WorkflowStepIds.CrossSubmissionEnrichment,
            WorkflowMessages.CrossSubmissionDisplayName,
            ValidationEngineKinds.ContextEnrichment,
            WorkflowMessages.CrossSubmissionDescription);

    public async Task<Result<WorkflowStepOutcome>> ExecuteAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var swStart = Stopwatch.GetTimestamp();

        // 1. Offline / Verify dry-run fixture injection
        if (TryExtractFixtureCrossSubmission(context.AmbientFixtures, out var fixtureCrossObj))
        {
            var outcome = await SubmissionExecutionScope.StepAsync(
                ProcessDescriptor,
                () =>
                {
                    InjectCrossSubmissionAmbient(context, fixtureCrossObj!);
                    var draftIds = ExtractDraftIdsFromFixture(fixtureCrossObj!);
                    return Task.FromResult(Result<EnrichmentEvalSummary>.Success(
                        new EnrichmentEvalSummary(
                            SiblingCount: ExtractSubmissionCount(fixtureCrossObj!),
                            DraftIds: draftIds,
                            Summary: WorkflowMessages.InjectedCrossSubmissionFixture)));
                },
                eval => new ProcessStepDetails(
                    Outcome: SubmissionProcessOutcome.Succeeded,
                    ItemCount: eval.SiblingCount,
                    Summary: eval.Summary));

            if (outcome.IsFailure)
            {
                return outcome.Error;
            }

            return ApplyDraftPolicyAndTrace(context, outcome.Value, swStart);
        }

        // 2. Skip cleanly when index repository is not configured (e.g. lightweight unit test pipelines)
        if (indexRepository is null)
        {
            RecordSkipped(context, WorkflowMessages.SkippedRepositoryNotConfigured);
            return new WorkflowStepOutcome(Succeeded: true, Summary: WorkflowMessages.SkippedNoIndexRepository);
        }

        // 3. Skip cleanly if no establishment identifier was extracted
        if (context.ExtractedIdentifier is not { } extractedId || string.IsNullOrWhiteSpace(extractedId.Value))
        {
            RecordSkipped(context, WorkflowMessages.SkippedNoIdentifierExtracted);
            return new WorkflowStepOutcome(Succeeded: true, Summary: WorkflowMessages.SkippedNoIdentifierShort);
        }

        // 4. Live identifier-index lookup across sibling submissions
        var stepResult = await SubmissionExecutionScope.StepAsync(
            ProcessDescriptor,
            async () =>
            {
                var entriesResult = await indexRepository.GetEntriesAsync(extractedId.Type, extractedId.Value, cancellationToken);
                if (entriesResult is null)
                {
                    return Result<EnrichmentEvalSummary>.Failure(
                        Error.Failure(ErrorCodes.StorageReadFailed, new { Message = WorkflowMessages.CrossSubmissionLookupFailed }));
                }

                if (entriesResult.IsFailure)
                {
                    return Result<EnrichmentEvalSummary>.Failure(entriesResult.Error);
                }

                var currentId = context.SubmissionId ?? Guid.Empty;
                var siblings = entriesResult.Value
                    .Where(e => e.SubmissionId != currentId)
                    .OrderByDescending(e => e.UpdatedAtUtc)
                    .ToList();

                var knownSiteIds = new HashSet<string>(StringComparer.Ordinal);
                var knownBlockIds = new HashSet<string>(StringComparer.Ordinal);
                var knownEvidenceIds = new HashSet<string>(StringComparer.Ordinal);
                var draftIds = new List<string>();
                var submissionsArray = new JsonArray();
                var bySchemaObj = new JsonObject();

                foreach (var entry in siblings)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var payloadResult = await indexRepository.ReadSiblingPayloadAsync(entry, cancellationToken);
                    if (payloadResult is null || payloadResult.IsFailure || string.IsNullOrWhiteSpace(payloadResult.Value))
                    {
                        continue;
                    }

                    JsonNode? parsedPayload;
                    try
                    {
                        parsedPayload = JsonNode.Parse(payloadResult.Value);
                    }
                    catch
                    {
                        continue;
                    }

                    if (parsedPayload is null)
                    {
                        continue;
                    }

                    var isDraft = string.Equals(entry.Status, "Draft", StringComparison.OrdinalIgnoreCase);
                    if (isDraft)
                    {
                        draftIds.Add(entry.SubmissionId.ToString());
                    }

                    var siteIdsForSibling = new JsonArray();
                    var blockIdsForSibling = new JsonArray();
                    HarvestEstateEntities(parsedPayload, siteIdsForSibling, blockIdsForSibling, knownSiteIds, knownBlockIds, knownEvidenceIds);

                    var siblingObj = new JsonObject
                    {
                        ["submissionId"] = entry.SubmissionId.ToString(),
                        ["schemaName"] = entry.SchemaName,
                        ["schemaVersion"] = entry.SchemaVersion,
                        ["status"] = entry.Status,
                        ["updatedAtUtc"] = entry.UpdatedAtUtc.ToString("O"),
                        ["siteIds"] = siteIdsForSibling,
                        ["blockIds"] = blockIdsForSibling,
                        ["payload"] = parsedPayload
                    };

                    submissionsArray.Add(siblingObj);

                    // Keep the newest submission per schema name in by_schema for clean O(1) lookup
                    if (!bySchemaObj.ContainsKey(entry.SchemaName))
                    {
                        bySchemaObj[entry.SchemaName] = siblingObj.DeepClone();
                    }
                }

                var crossObj = new JsonObject
                {
                    ["identifierType"] = extractedId.Type,
                    ["identifierValue"] = extractedId.Value,
                    ["known_site_ids"] = ToJsonArray(knownSiteIds),
                    ["known_block_ids"] = ToJsonArray(knownBlockIds),
                    ["known_evidence_ids"] = ToJsonArray(knownEvidenceIds),
                    ["has_draft_references"] = draftIds.Count > 0,
                    ["draft_submission_ids"] = ToJsonArray(draftIds),
                    ["submissions"] = submissionsArray,
                    ["by_schema"] = bySchemaObj
                };

                InjectCrossSubmissionAmbient(context, crossObj);

                var summary = submissionsArray.Count == 0
                    ? WorkflowMessages.NoSiblingSubmissionsFound(extractedId.Type, extractedId.Value)
                    : WorkflowMessages.LoadedSiblingSubmissions(submissionsArray.Count, extractedId.Type, extractedId.Value, draftIds.Count);

                return Result<EnrichmentEvalSummary>.Success(new EnrichmentEvalSummary(submissionsArray.Count, draftIds, summary));
            },
            eval => new ProcessStepDetails(
                Outcome: SubmissionProcessOutcome.Succeeded,
                ItemCount: eval.SiblingCount,
                Summary: eval.Summary),
            err => new ProcessStepDetails(
                Outcome: SubmissionProcessOutcome.Failed,
                Summary: err.Description ?? err.Code,
                ErrorCode: err.Code));

        if (stepResult.IsFailure)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(swStart).TotalMilliseconds;
            context.EngineTraces.Add(new ValidationEngineTrace(
                Descriptor: EngineDescriptor,
                Outcome: nameof(SubmissionProcessOutcome.Failed),
                ItemsEvaluated: 0,
                ViolationCount: 1,
                DurationMs: elapsedMs,
                Summary: stepResult.Error.Description ?? stepResult.Error.Code));
            return stepResult.Error;
        }

        return ApplyDraftPolicyAndTrace(context, stepResult.Value, swStart);
    }

    public void RecordSkipped(WorkflowContext context, string reason)
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

    private WorkflowStepOutcome ApplyDraftPolicyAndTrace(
        WorkflowContext context,
        EnrichmentEvalSummary eval,
        long swStart)
    {
        var elapsedMs = (long)Stopwatch.GetElapsedTime(swStart).TotalMilliseconds;
        var issueCount = 0;
        var succeeded = true;

        if (context.OperationKind == WorkflowOperationKind.SubmissionFinalise && eval.DraftIds.Count > 0)
        {
            var pointer = context.Schema.Metadata.IdentifierConfig?.JsonPointer ?? context.Options.DefaultIdentifierPointerFallback;
            var draftList = string.Join(", ", eval.DraftIds);

            switch (context.Policy.CrossSubmissionDraftPolicy)
            {
                case CrossSubmissionDraftPolicy.BlockFinaliseOnDraftReferences:
                {
                    var msg = WorkflowMessages.BlockFinaliseOnDraftReferences(draftList);
                    var err = new ValidationError(
                        PropertyPath: pointer,
                        Message: msg,
                        RuleId: "CrossSubmission.DraftReferenceBlocked",
                        Severity: RuleSeverity.Error,
                        Action: "reject_submission",
                        EngineId: StepId);
                    context.AllIssues.Add(err);
                    issueCount++;
                    succeeded = !RuleSeverity.Error.IsAtOrAbove(context.Policy.FailOnMinimumSeverity);
                    break;
                }
                case CrossSubmissionDraftPolicy.WarnOnDraftReferences:
                {
                    var msg = WorkflowMessages.WarnOnDraftReferences(draftList);
                    var warn = new ValidationError(
                        PropertyPath: pointer,
                        Message: msg,
                        RuleId: "CrossSubmission.DraftReferenceWarning",
                        Severity: RuleSeverity.Warning,
                        Action: "highlight_issue",
                        EngineId: StepId);
                    context.AllIssues.Add(warn);
                    issueCount++;
                    succeeded = !RuleSeverity.Warning.IsAtOrAbove(context.Policy.FailOnMinimumSeverity);
                    break;
                }
                case CrossSubmissionDraftPolicy.AllowDraftReferences:
                    break;
            }
        }

        context.EngineTraces.Add(new ValidationEngineTrace(
            Descriptor: EngineDescriptor,
            Outcome: issueCount > 0 && !succeeded
                ? nameof(SubmissionProcessOutcome.ValidationErrorsFound)
                : nameof(SubmissionProcessOutcome.Succeeded),
            ItemsEvaluated: eval.SiblingCount,
            ViolationCount: issueCount,
            DurationMs: elapsedMs,
            Summary: eval.Summary));

        return new WorkflowStepOutcome(Succeeded: succeeded, Summary: eval.Summary);
    }

    private static bool TryExtractFixtureCrossSubmission(
        IReadOnlyDictionary<string, JsonNode?>? fixtures,
        out JsonObject? crossObj)
    {
        crossObj = null;
        if (fixtures is null)
        {
            return false;
        }

        if (fixtures.TryGetValue("cross_submission", out var direct) && direct is JsonObject directObj)
        {
            crossObj = directObj.DeepClone().AsObject();
            return true;
        }

        if (fixtures.TryGetValue("ambient", out var ambientNode) &&
            ambientNode is JsonObject ambientObj &&
            ambientObj.TryGetPropertyValue("cross_submission", out var nested) &&
            nested is JsonObject nestedObj)
        {
            crossObj = nestedObj.DeepClone().AsObject();
            return true;
        }

        return false;
    }

    private static void InjectCrossSubmissionAmbient(WorkflowContext context, JsonObject crossObj)
    {
        context.AmbientContext["cross_submission"] = crossObj.DeepClone();

        var ambientRoot = context.AmbientContext.TryGetValue("ambient", out var existingAmbient) && existingAmbient is JsonObject existingObj
            ? existingObj
            : new JsonObject();
        ambientRoot["cross_submission"] = crossObj;
        context.AmbientContext["ambient"] = ambientRoot;
    }

    private static void HarvestEstateEntities(
        JsonNode rootNode,
        JsonArray siblingSites,
        JsonArray siblingBlocks,
        HashSet<string> knownSiteIds,
        HashSet<string> knownBlockIds,
        HashSet<string> knownEvidenceIds)
    {
        if (rootNode["sites"] is JsonArray sites)
        {
            foreach (var site in sites)
            {
                if (TryGetStringProperty(site, "id", "siteId", "site_id", "name") is { } siteKey &&
                    knownSiteIds.Add(siteKey))
                {
                    siblingSites.Add(siteKey);
                }

                if (site?["blocks"] is JsonArray nestedBlocks)
                {
                    HarvestBlocks(nestedBlocks, siblingBlocks, knownBlockIds);
                }
            }
        }

        if (rootNode["blocks"] is JsonArray topBlocks)
        {
            HarvestBlocks(topBlocks, siblingBlocks, knownBlockIds);
        }

        if (rootNode["evidence"] is JsonArray evidenceList)
        {
            foreach (var ev in evidenceList)
            {
                if (TryGetStringProperty(ev, "id", "evidenceId", "evidence_id") is { } evId)
                {
                    knownEvidenceIds.Add(evId);
                }
            }
        }
    }

    private static void HarvestBlocks(JsonArray blocks, JsonArray siblingBlocks, HashSet<string> knownBlockIds)
    {
        foreach (var block in blocks)
        {
            if (TryGetStringProperty(block, "id", "blockId", "block_id", "name") is { } blockKey &&
                knownBlockIds.Add(blockKey))
            {
                siblingBlocks.Add(blockKey);
            }
        }
    }

    private static string? TryGetStringProperty(JsonNode? node, params string[] keys)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (obj.TryGetPropertyValue(key, out var valNode) &&
                valNode is JsonValue jv &&
                jv.TryGetValue<string>(out var val) &&
                !string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }

        return null;
    }

    private static List<string> ExtractDraftIdsFromFixture(JsonObject crossObj)
    {
        var ids = new List<string>();
        if (crossObj.TryGetPropertyValue("draft_submission_ids", out var draftArrNode) && draftArrNode is JsonArray dArr)
        {
            foreach (var item in dArr)
            {
                if (item is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                {
                    ids.Add(s);
                }
            }
        }

        if (ids.Count == 0 &&
            crossObj.TryGetPropertyValue("submissions", out var subsNode) &&
            subsNode is JsonArray subsArr)
        {
            foreach (var item in subsArr)
            {
                if (item is JsonObject obj &&
                    obj.TryGetPropertyValue("status", out var statusNode) &&
                    string.Equals(statusNode?.ToString(), "Draft", StringComparison.OrdinalIgnoreCase))
                {
                    var id = obj["submissionId"]?.ToString() ?? "fixture-draft";
                    ids.Add(id);
                }
            }
        }

        if (ids.Count == 0 &&
            crossObj.TryGetPropertyValue("has_draft_references", out var flagNode) &&
            flagNode is JsonValue jvFlag &&
            jvFlag.TryGetValue<bool>(out var hasDraft) &&
            hasDraft)
        {
            ids.Add("fixture-draft");
        }

        return ids;
    }

    private static int ExtractSubmissionCount(JsonObject crossObj)
    {
        return crossObj.TryGetPropertyValue("submissions", out var node) && node is JsonArray arr
            ? arr.Count
            : 1;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values)
        {
            arr.Add(v);
        }
        return arr;
    }

    private sealed record EnrichmentEvalSummary(int SiblingCount, IReadOnlyList<string> DraftIds, string Summary);
}
