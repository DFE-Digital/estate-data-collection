namespace EstateDataCollection.Common.Messages;

public static class AdminUiMessages
{
    public const string DashboardTitle = "Schema Contracts & Lifecycle";
    public static string ScheduleSchemaTitle(string schemaName, int version) => $"Schedule {schemaName} v{version}";
    public static string TestsPageTitle(string schemaName, int version) => $"Tests - {schemaName} v{version}";
    public const string StatisticsTitle = "Submission Statistics";
    public static string SubmissionDetailTitle(Guid submissionId) => $"Submission {submissionId}";
    public const string SubmissionsTitle = "Submissions";
    public const string WorkbenchTitle = "Contract Workbench";

    public static string NoticeSchemaSubmitted(string schemaName, int version) =>
        $"Schema '{schemaName}' v{version} passed verification and was saved as Validated (unscheduled).";
    public static string NoticeSchemaScheduled(string schemaName, int version, string stateName) =>
        $"Lifecycle & schedule updated for '{schemaName}' v{version} (current state: {stateName}).";
    public static string NoticeSchemaSignedOff(string schemaName, int version, string? signedOffBy) =>
        $"Schema '{schemaName}' v{version} was signed off by '{signedOffBy}' and transitioned to Validated.";
    public static string NoticeSchemaDeleted(string schemaName, int version) =>
        $"Schema '{schemaName}' v{version} was deleted. Metadata and error report tombstones remain reserved; any fix must be published as v{version + 1}.";
    public static string NoticeSchemaTestsUpdated(string schemaName, int version) =>
        $"Stored regression test payloads verified and saved for '{schemaName}' v{version}.";
    public static string NoticeAllSchemaTestsPassed(int passedCount, string appVersion) =>
        $"All {passedCount} schema(s) passed regression tests on build {appVersion}.";

    public const string EnterIdentifierPointerOrNone =
        "Enter a JSON Pointer path (e.g. '/school/urn' or '/establishment/laestab'), select a property from the schema, or choose 'None'.";
    public const string ProvideCustomIdentifierTypeOrPointer =
        "Provide a custom identifier type label or JSON Pointer path.";
    public const string VersionMustBeAtLeastOne = "Version must be an integer greater than or equal to 1.";
    public const string SchemaNameRequiredWhenSaving = "Schema name is required when saving a schema version.";
    public const string ProvideOdcsYamlContent = "Provide ODCS v3 YAML contract content.";
    public const string ProvideJsonSchemaContent = "Provide JSON Schema content.";
    public const string WorkbenchFormInvalid = "One or more workbench fields are invalid.";
    public const string FixturePayloadMustBeObjectOrArray = "Fixture payload must be a JSON object or JSON array of objects.";
    public const string FixtureMustBeObjectOrArrayShort = "Must be a JSON object or JSON array of objects.";
    public static string InvalidJsonDetail(string exMessage) => $"Invalid JSON: {exMessage}";
    public const string EffectiveFromInvalidUtc = "Effective from must be a valid UTC date/time (YYYY-MM-DDTHH:MM).";
    public const string EffectiveToInvalidUtc = "Effective to must be a valid UTC date/time (YYYY-MM-DDTHH:MM).";
    public const string InvalidScheduleInput = "Invalid schedule input.";
    public const string InvalidTestJsonPayloads = "One or more test JSON payloads are invalid.";

    public static string VerifiedPassedCountSummary(int passedCount, int totalSchemasChecked) =>
        $"{passedCount} / {totalSchemasChecked} Passed";
    public static string VerifiedPassedWarningsCountSummary(int passedCount, int totalSchemasChecked) =>
        $"{passedCount} / {totalSchemasChecked} Passed (warnings)";
    public static string VerifiedFailedCountSummary(int failedCount, int passedCount) =>
        $"{failedCount} Failed ({passedCount} Passed)";
    public const string TagPassed = "Passed";
    public const string TagPassedWarnings = "Passed (warnings)";
    public const string TagFailed = "Failed";
    public static string TestRunCountsSummary(int validCount, int invalidCount, int ruleCount) =>
        $"{validCount} valid, {invalidCount} invalid, {ruleCount} rule(s)";

    public static string UnscheduledVersionsBannerMessage(int count) =>
        $"{count} validated schema version(s) are currently unscheduled.";
    public const string UnscheduledVersionsBannerTitle = "New version(s) ready to schedule";
    public const string UnscheduledVersionsBannerDetail =
        "Contracts promoted from draft-schemas or submitted via the workbench remain in Validated state until you configure an effective window or activate them.";
    public static string ErrorReportAttentionBannerMessage(int totalCount, int invalidCount, int unvalidatedCount) =>
        $"{totalCount} schema version(s) have a persisted .error.json diagnostic report ({invalidCount} Invalid, {unvalidatedCount} awaiting sign-off).";
    public const string ErrorReportAttentionBannerTitle = "Schema diagnostics require review";
    public const string ErrorReportAttentionBannerDetail =
        "Inspect highlighted versions to review .error.json findings, sign off Unvalidated contracts with warnings, or delete broken versions so a fix can be published to version + 1.";

    public static string TopLevelFieldSummary(int count) =>
        $"{count} top-level {(count == 1 ? "field" : "fields")}";
    public static string ActiveRuleSummary(int count) =>
        $"{count} active {(count == 1 ? "rule" : "rules")}";

    public const string IdentifierPointerNoneDisabled = "None (disabled)";
    public const string IdentifierPointerNotSet = "Not set — enter or select a property";
    public const string IdentifierLabelNone = "None";
    public const string IdentifierLabelDerivedFromProperty = "Derived from property name";
    public const string IdentifierRequiredNoDisabled = "No (identifier partitioning disabled)";
    public const string IdentifierRequiredYesSchema = "Yes (required by JSON Schema)";
    public const string IdentifierRequiredNoSchema = "No (optional in JSON Schema)";
    public const string IdentifierRequiredAutoDerived = "Derived automatically from JSON Schema 'required' on Verify / Save";
    public static string IdentifierLabelAutoPlaceholder(string autoDerivedLabel) => $"Auto: {autoDerivedLabel}";
    public const string IdentifierLabelPlaceholderFallback = "Derived from property";
    public static string IdentifierOptionDisplay(string pointer, string valueType, bool required) =>
        $"{pointer} ({valueType}, {(required ? "required" : "optional")})";

    public const string BannerHeadingSuccess = "Success";
    public const string BannerHeadingImportant = "Important";
    public const string LifecycleStateUndetermined = "Undetermined";

    public static string BackToSchemaDetails(string schemaName, int version) => $"Back to {schemaName} V{version}";
    public static string ManageSchemaHeading(string schemaName, int version) => $"Manage {schemaName}, V{version}";

    public const string AcceptsTrafficYes = "Yes (public submissions permitted)";
    public const string AcceptsTrafficNo = "No";
    public const string UnscheduledLabel = "Unscheduled";
    public const string OpenEndedLabel = "Open-ended";
    public const string IdentifierRequiredYes = "Yes";
    public const string IdentifierRequiredNo = "No";
    public const string IdentifierRequiredDefault = "Default";
    public const string DefaultLabel = "Default";
    public const string NoneLabel = "None";
    public const string ErrorReportHeadingFailed = "Schema Error Report (.error.json) — Validation Failed";
    public const string ErrorReportHeadingWarnings = "Schema Diagnostic Report (.error.json) — Warnings Recorded";
    public const string ErrorReportTagInvalid = "Invalid (.error.json)";
    public const string ErrorReportTagErrors = "Errors (.error.json)";
    public const string ErrorReportTagWarnings = "Warnings (.error.json)";
    public static string SchemaFieldsTabLabel(int count) => $"Fields ({count})";
    public static string SchemaQualityRulesTabLabel(int count) => $"Quality rules ({count})";
    public const string SchemaValidationEnginesTabLabel = "Validation engines";
    public const string SchemaRawJsonTabLabel = "Raw JSON Schema";

    public static string SignedOffByWithTimestamp(string? signedOffBy, DateTimeOffset timestampUtc) =>
        $"Signed off by {signedOffBy} ({timestampUtc:yyyy-MM-dd HH:mm} UTC)";
    public static string SignedOffBy(string? signedOffBy) => $"Signed off by {signedOffBy}";
    public static string DeletedWithTimestamp(DateTimeOffset deletedAtUtc, int nextVersion) =>
        $"Deleted {deletedAtUtc:yyyy-MM-dd HH:mm} UTC (push fixes to v{nextVersion})";
    public static string DeletedWithoutTimestamp(int nextVersion) =>
        $"Deleted (push fixes to v{nextVersion})";
    public const string TestStatusTombstone = "Tombstone";
    public const string TestStatusNotRun = "Not run";

    public const string NoSubmissionsYet = "No submissions yet";

    public const string EngineDiagnosticsDefaultHeading = "Validation Engine Diagnostics";
    public const string EngineDiagnosticsDefaultSubtitle =
        "Per-engine execution metrics and outcomes from the latest regression test run.";
    public const string RegisteredEnginesDefaultHeading = "Registered Validation Engines";
    public static string EngineDiagnosticsTraceCountSubtitle(int count) =>
        $"Execution outcomes, evaluated items, violations, and timings recorded across {count} validation engine(s).";
    public const string LastTestRunEngineDiagnosticsHeading = "Last Test Run Engine Diagnostics";
    public static string LastTestRunEngineDiagnosticsSubtitle(int count) =>
        $"Execution outcomes, evaluated items, violations, and timings recorded across {count} validation engine(s) in the latest regression test run.";
    public static string RegisteredEnginesApplicabilitySubtitle(int applicableCount, int totalCount) =>
        $"All validation engines currently registered in this environment and whether they apply to this schema version ({applicableCount} of {totalCount} active or applicable).";
    public const string EngineApplicableLabel = "Applicable";
    public const string EngineDisabledLabel = "Disabled";
    public static string EngineActiveRulesLabel(int matchingCount) =>
        $"Active ({matchingCount} rule{(matchingCount == 1 ? string.Empty : "s")})";
    public const string EngineIdleZeroRulesLabel = "Idle (0 rules)";
    public const string EngineKindStructuralSchema = "Structural Schema";
    public const string EngineKindQualityRule = "Quality Rule";
    public const string EngineKindIdentifierExtraction = "Identifier Extraction";
    public const string UnknownLabel = "Unknown";

    public static string EvaluatedFixturesSummary(int validCount, int invalidCount, int ruleCount) =>
        $"Evaluated {validCount} valid fixture(s), {invalidCount} invalid fixture(s), and {ruleCount} business rule(s).";
    public static string ValidFixtureTitle(int number) => $"Valid Fixture #{number}";
    public static string InvalidFixtureTitle(int number) => $"Invalid Fixture #{number}";
    public static string ValidFixturesTabLabel(int count) => $"Valid fixtures ({count})";
    public static string InvalidFixturesTabLabel(int count) => $"Invalid fixtures ({count})";
    public const string EngineDiagnosticsTabLabel = "Engine diagnostics";
    public const string EditSchemaTestsTabLabel = "Add / update tests";
    public static string QualityRulesTabLabel(int count) => $"Quality rules ({count})";
    public const string FixtureStatusPassedUnexpectedly = "Passed unexpectedly";
    public const string FixtureStatusExpectationMismatch = "Expectation mismatch";
    public const string FixtureStatusMatchedExpectedErrors = "Matched expected error(s)";
    public const string FixtureStatusFailedUnknownIfExpected = "Failed \u2014 unknown if expected";
    public const string FixtureStatusPendingEvaluation = "Pending evaluation";
    public const string AssertionConfigured = "Configured";
    public const string AssertionMissing = "Missing";
    public const string AssertionMatched = "Matched";

    public const string SubmissionFinalisedText = "Finalised";
    public const string SubmissionDraftPayloadSavedText = "Draft (payload saved)";
    public const string SubmissionDraftAwaitingPayloadText = "Draft (awaiting payload)";
    public const string SubmissionReadinessLocked = "Locked (Finalised)";
    public const string SubmissionReadinessReady = "Ready to finalise";
    public const string SubmissionReadinessNotReady = "Not ready (pending issues)";
    public const string IdentifierUnassigned = "Unassigned";
    public static string SubmissionAuditTrailTabLabel(int count) => $"Audit trail ({count})";
    public const string SubmissionPayloadSessionTabLabel = "Payload & session";
    public static string SubmissionDraftUploadsTabLabel(int count) => $"Draft uploads ({count})";

    public const string FilterStatusAllStates = "All states";
    public const string FilterStatusDraftAllInProgress = "Draft (all in-progress)";
    public const string FilterStatusFinalisedReadyForCde = "Finalised (ReadyForCde)";

    public const string VerificationPassedTitle = "Verification passed";
    public const string VerificationPassedDetail =
        "Contract syntax, identifier alignment, quality rules, and any provided sample fixtures passed dry-run verification.";
    public const string VerifyEngineDiagnosticsSubtitle =
        "Per-engine execution outcomes across schema syntax, quality rules, identifier extraction, and golden fixtures.";
    public const string VerifySummaryTabLabel = "Summary & fixtures";
    public const string VerifyEngineDiagnosticsTabLabel = "Engine diagnostics";
}
