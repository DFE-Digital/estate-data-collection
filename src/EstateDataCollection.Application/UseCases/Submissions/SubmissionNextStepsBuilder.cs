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
                ? $"Submission finalised at {finalisedAtUtc.Value.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC and promoted for downstream processing."
                : "Submission has been finalised and promoted for downstream processing.";

            return new Guidance(
                Summary: finalisedText,
                NextSteps:
                [
                    "No further action is required. This submission is locked and cannot be modified."
                ]);
        }

        if (!hasDraftPayload)
        {
            return new Guidance(
                Summary: "Draft session is active, but no payload sections have been uploaded yet.",
                NextSteps:
                [
                    $"Upload an initial JSON draft payload via PUT {updateUrl}.",
                    $"Run GET {checkUrl} after uploading to preview merged validation readiness."
                ]);
        }

        var mergedPrefix = draftCount > 1
            ? $"Merged {draftCount} timestamped draft submissions; "
            : draftCount == 1
                ? "Evaluated 1 draft submission; "
                : "Evaluated draft submission; ";

        if (pendingErrors.Count == 0)
        {
            return new Guidance(
                Summary: $"{mergedPrefix}all schema, quality rule, and identifier checks passed.",
                NextSteps:
                [
                    $"Submission is ready to finalise. Call POST {finaliseUrl} to validate and promote the merged payload."
                ]);
        }

        var steps = new List<string>(Math.Min(pendingErrors.Count, 5) + 3);
        const int maxListedIssues = 5;

        foreach (var err in pendingErrors.Take(maxListedIssues))
        {
            var rulePrefix = !string.IsNullOrWhiteSpace(err.RuleId) ? $"[{err.RuleId}] " : string.Empty;
            var path = string.IsNullOrWhiteSpace(err.PropertyPath) ? "$" : err.PropertyPath;
            steps.Add($"Resolve {rulePrefix}{path}: {err.Message}");
        }

        if (pendingErrors.Count > maxListedIssues)
        {
            steps.Add($"Resolve the remaining {pendingErrors.Count - maxListedIssues} issue(s) listed in pendingErrors.");
        }

        steps.Add($"Upload an updated or partial JSON patch via PUT {updateUrl} (timestamped drafts are merged automatically).");
        steps.Add($"Re-check readiness via GET {checkUrl} before calling POST {finaliseUrl}.");

        return new Guidance(
            Summary: $"{mergedPrefix}{pendingErrors.Count} pending issue(s) must be resolved before finalising.",
            NextSteps: steps);
    }
}
