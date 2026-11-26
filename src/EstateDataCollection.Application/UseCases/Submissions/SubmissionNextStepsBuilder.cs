using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;

namespace EstateDataCollection.Application.UseCases.Submissions;

public static class SubmissionNextStepsBuilder
{
    public sealed record Guidance(string Summary, IReadOnlyList<string> NextSteps);

    public static Guidance Build(
        Guid submissionId,
        bool isFinalised,
        DateTimeOffset? finalisedAtUtc,
        bool hasDraftPayload,
        int draftCount,
        IReadOnlyList<ValidationError> pendingErrors)
    {
        var updateUrl = AppRoutes.PublicApi.UpdateSubmission(submissionId);
        var checkUrl = AppRoutes.PublicApi.CheckSubmission(submissionId);
        var finaliseUrl = AppRoutes.PublicApi.FinaliseSubmission(submissionId);

        if (isFinalised)
        {
            var finalisedText = finalisedAtUtc.HasValue
                ? SubmissionMessages.FinalisedAtUtc(finalisedAtUtc.Value)
                : SubmissionMessages.FinalisedWithoutTimestamp;

            return new Guidance(
                Summary: finalisedText,
                NextSteps:
                [
                    SubmissionMessages.FinalisedNoFurtherAction
                ]);
        }

        if (!hasDraftPayload)
        {
            return new Guidance(
                Summary: SubmissionMessages.DraftActiveNoPayload,
                NextSteps:
                [
                    SubmissionMessages.UploadInitialDraftStep(updateUrl),
                    SubmissionMessages.RunCheckAfterUploadStep(checkUrl)
                ]);
        }

        var mergedPrefix = draftCount > 1
            ? SubmissionMessages.MergedDraftsPrefix(draftCount)
            : draftCount == 1
                ? SubmissionMessages.SingleDraftPrefix
                : SubmissionMessages.GenericDraftPrefix;

        if (pendingErrors.Count == 0)
        {
            return new Guidance(
                Summary: SubmissionMessages.AllChecksPassedSummary(mergedPrefix),
                NextSteps:
                [
                    SubmissionMessages.ReadyToFinaliseStep(finaliseUrl)
                ]);
        }

        var steps = new List<string>(Math.Min(pendingErrors.Count, 5) + 3);
        const int maxListedIssues = 5;

        foreach (var err in pendingErrors.Take(maxListedIssues))
        {
            var rulePrefix = !string.IsNullOrWhiteSpace(err.RuleId) ? $"[{err.RuleId}] " : string.Empty;
            var path = string.IsNullOrWhiteSpace(err.PropertyPath) ? "$" : err.PropertyPath;
            steps.Add(SubmissionMessages.ResolveIssueStep(rulePrefix, path, err.Message));
        }

        if (pendingErrors.Count > maxListedIssues)
        {
            steps.Add(SubmissionMessages.ResolveRemainingIssuesStep(pendingErrors.Count - maxListedIssues));
        }

        steps.Add(SubmissionMessages.UploadUpdatedPatchStep(updateUrl));
        steps.Add(SubmissionMessages.ReCheckBeforeFinaliseStep(checkUrl, finaliseUrl));

        return new Guidance(
            Summary: SubmissionMessages.PendingIssuesSummary(mergedPrefix, pendingErrors.Count),
            NextSteps: steps);
    }
}
