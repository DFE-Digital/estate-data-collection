namespace EstateDataCollection.Common.Messages;

public static class SubmissionMessages
{
    public static string SchemaVersionCannotAcceptTraffic(int version) =>
        $"Schema v{version} cannot accept traffic.";
    public static string CreatedSessionSummary(Guid submissionId) =>
        $"Created session {submissionId}";
    public static string DraftSessionCreatedAudit(string schemaName, int version) =>
        $"Draft session created for {schemaName} v{version}";

    public const string SessionAlreadyFinalised = "Session already finalised";
    public const string SessionFinalised = "Session finalised";
    public const string DraftSessionLoaded = "Draft session loaded";
    public static string SchemaRetiredNoUpdates(string name, int version) =>
        $"Schema '{name}' v{version} is retired and no longer accepts updates.";
    public static string SchemaRetiredNoFinalisation(string name, int version) =>
        $"Schema '{name}' v{version} is retired and no longer accepts finalisation.";
    public static string SavedDraftChunkSummary(int draftCountAfterSave, int payloadLength) =>
        $"Saved timestamped draft chunk #{draftCountAfterSave} ({payloadLength} chars)";
    public static string DraftSavedNoErrors(int draftCountAfterSave) =>
        $"Draft payload saved (chunk #{draftCountAfterSave}) with no validation errors";
    public static string DraftSavedWithPendingErrors(int draftCountAfterSave, int errorCount) =>
        $"Draft payload saved (chunk #{draftCountAfterSave}) with {errorCount} pending validation error(s)";

    public static string FinalisationRejectedSummary(int errorCount) =>
        $"Finalisation rejected with {errorCount} validation error(s)";
    public const string IdentifierInvalidFallback = "Identifier invalid";
    public const string PromotedMergedDraftSummary = "Promoted merged draft payload to processed container";
    public const string SubmissionFinalisedAuditMessage = "Submission finalised and promoted to processed storage";

    public const string NoDraftPayloadUploadedYet = "No draft payload has been uploaded yet.";
    public const string NoDraftPayloadUploadedShort = "No draft payload uploaded yet";
    public const string NoTimestampedDraftChunks = "No timestamped draft chunks listed";
    public static string LoadedDraftChunks(int count) => $"Loaded {count} draft chunk(s)";
    public static string MergedTimestampedDrafts(int count) => $"Merged {count} timestamped draft submission(s)";
    public static string LoadedDraftJson(int length) => $"Loaded {length} char draft JSON";
    public static string MergedIncomingPatch(int priorCount) =>
        $"Merged incoming patch with {priorCount} prior draft chunk(s)";

    public static string FinalisedAtUtc(DateTimeOffset finalisedAtUtc) =>
        $"Submission finalised at {finalisedAtUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC and promoted for downstream processing.";
    public const string FinalisedWithoutTimestamp =
        "Submission has been finalised and promoted for downstream processing.";
    public const string FinalisedNoFurtherAction =
        "No further action is required. This submission is locked and cannot be modified.";
    public const string DraftActiveNoPayload =
        "Draft session is active, but no payload sections have been uploaded yet.";
    public static string UploadInitialDraftStep(string updateUrl) =>
        $"Upload an initial JSON draft payload via PUT {updateUrl}.";
    public static string RunCheckAfterUploadStep(string checkUrl) =>
        $"Run GET {checkUrl} after uploading to preview merged validation readiness.";
    public static string MergedDraftsPrefix(int draftCount) =>
        $"Merged {draftCount} timestamped draft submissions; ";
    public const string SingleDraftPrefix = "Evaluated 1 draft submission; ";
    public const string GenericDraftPrefix = "Evaluated draft submission; ";
    public static string AllChecksPassedSummary(string prefix) =>
        $"{prefix}all schema, quality rule, and identifier checks passed.";
    public static string ReadyToFinaliseStep(string finaliseUrl) =>
        $"Submission is ready to finalise. Call POST {finaliseUrl} to validate and promote the merged payload.";
    public static string ResolveIssueStep(string rulePrefix, string path, string message) =>
        $"Resolve {rulePrefix}{path}: {message}";
    public static string ResolveRemainingIssuesStep(int remainingCount) =>
        $"Resolve the remaining {remainingCount} issue(s) listed in pendingErrors.";
    public static string UploadUpdatedPatchStep(string updateUrl) =>
        $"Upload an updated or partial JSON patch via PUT {updateUrl} (timestamped drafts are merged automatically).";
    public static string ReCheckBeforeFinaliseStep(string checkUrl, string finaliseUrl) =>
        $"Re-check readiness via GET {checkUrl} before calling POST {finaliseUrl}.";
    public static string PendingIssuesSummary(string prefix, int pendingCount) =>
        $"{prefix}{pendingCount} pending issue(s) must be resolved before finalising.";
}
