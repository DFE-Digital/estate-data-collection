namespace EstateDataCollection.Common.Messages;

public static class WorkflowMessages
{
    public static string WorkflowPipelineNotRegistered(string pipelineKey, object operationKind) =>
        $"Workflow pipeline '{pipelineKey}' is not registered for operation '{operationKind}'.";
    public static string RequiredWorkflowStepNotRegistered(string stepId) =>
        $"Required workflow step '{stepId}' is not registered.";
    public const string SkippedStructuralSchemaFailed = "Skipped because structural JSON Schema validation failed";
    public static string SkippedPrerequisiteStepFailed(string depStepId) =>
        $"Skipped because prerequisite step '{depStepId}' did not succeed";
    public const string SkippedCriticalIssue = "Skipped because an earlier step emitted a Critical issue";
    public const string SkippedBlockingIssues = "Skipped because an earlier step emitted blocking validation issues";
    public const string SkippedPreviousStepFailed = "Skipped because the previous workflow step did not succeed";

    public const string JsonSchemaDisplayName = "JSON Schema Validator";
    public const string JsonSchemaDescription = "Structural JSON Schema validator.";
    public const string SchemaStructureValid = "Schema structure valid";
    public static string SchemaErrorCount(int count) => $"{count} schema error(s)";

    public const string SkippedDuringDraftUpdateDetailed = "Skipped during draft update per ValidationOptions";
    public const string SkippedDuringDraftUpdateShort = "Skipped during draft update";
    public const string IdentifierNotRequiredBySchema = "Not required by schema";
    public const string IdentifierDisabledBySchema = "Identifier extraction disabled by schema";

    public const string CrossSubmissionDisplayName = "Cross-Submission Enrichment";
    public const string CrossSubmissionDescription =
        "Loads sibling submissions sharing the extracted establishment identifier (draft + finalised) and enforces draft-reference policies.";
    public const string InjectedCrossSubmissionFixture = "Injected cross-submission ambient fixture";
    public const string SkippedRepositoryNotConfigured = "Cross-submission index repository is not configured";
    public const string SkippedNoIndexRepository = "Skipped (no index repository)";
    public const string SkippedNoIdentifierExtracted = "No establishment identifier extracted for cross-submission lookup";
    public const string SkippedNoIdentifierShort = "Skipped (no identifier)";
    public const string CrossSubmissionLookupFailed = "Failed to look up sibling submissions in identifier index.";
    public static string NoSiblingSubmissionsFound(string type, string value) =>
        $"No sibling submissions found for {type}={value}";
    public static string LoadedSiblingSubmissions(int count, string type, string value, int draftCount) =>
        $"Loaded {count} sibling submission(s) for {type}={value} ({draftCount} draft)";
    public static string BlockFinaliseOnDraftReferences(string draftList) =>
        $"Cannot finalise submission while sibling draft submission(s) remain unfinalised for this establishment: {draftList}.";
    public static string WarnOnDraftReferences(string draftList) =>
        $"Cross-submission context includes unfinalised sibling draft submission(s): {draftList}.";

    public const string ReferenceDataDisplayName = "Reference Data Enrichment";
    public const string ReferenceDataDescription =
        "Loads versioned reference datasets from blob storage, local paths, or verify fixtures into ambient.reference.";
    public const string SkippedProviderNotConfigured = "Reference data provider is not configured";
    public const string SkippedNoReferenceProvider = "Skipped (no reference data provider)";
    public const string NoReferenceDatasetsRegistered = "No reference datasets registered for schema";
    public static string LoadedReferenceDatasetsIntoAmbient(int count) =>
        $"Loaded {count} reference dataset key(s) into ambient.reference";
    public static string LoadedReferenceDatasetsShort(int count) =>
        $"Loaded {count} reference dataset key(s)";

    public const string QualityRulesCoordinatorDisplayName = "Quality Rules Coordinator";
    public const string QualityRulesCoordinatorDescription =
        "Coordinates ordered quality rule execution across registered engines.";
    public const string NoQualityRulesConfigured = "No quality rules configured";
    public static string EvaluatedQualityRulesZeroFailures(int count) =>
        $"Evaluated {count} quality rule(s) with 0 failures";
    public static string EvaluatedQualityRulesAdvisory(int ruleCount, int issueCount) =>
        $"Evaluated {ruleCount} quality rule(s); {issueCount} advisory/warning issue(s)";
    public static string EvaluatedQualityRulesBlocking(int ruleCount, int blockingCount) =>
        $"Evaluated {ruleCount} quality rule(s); {blockingCount} blocking violation(s)";
    public static string QualityRuleEngineDescription(string engineId) =>
        $"Quality rule engine '{engineId}'.";

    public const string JsonSchemaNetDisplayName = "JsonSchema.Net Validator";
    public static string JsonSchemaNetDescription(bool requireFormat) =>
        $"Validates payload structure against compiled JSON Schema (Draft 2020-12 / Draft-07) with closed-schema enforcement (format validation={requireFormat}).";

    public const string RulesJsonEmptyOrNull = "Rules JSON was empty or null.";
    public const string RulesJsonMustContainRule = "Rules JSON must contain at least one business rule.";
    public static string MalformedRulesJsonSyntax(string exMessage) =>
        $"Malformed Rules JSON syntax: {exMessage}";
    public const string RuleIdRequired = "'id' is required.";
    public const string RuleDescriptionRequired = "'description' is required.";
    public const string RulePathRequired = "'path' target is required.";
    public const string RuleMessageRequired = "'message' is required.";
    public const string RuleAssertionRequired = "'assert' (or 'implementation') logic condition is required.";
    public static string RuleErrorPrefixed(string? ruleId, string? engineError) =>
        $"Rule '{ruleId}': {engineError}";
    public static string UnknownEngineSkippedSummary(string engineName) =>
        $"Engine '{engineName}' skipped per UnknownEngineBehavior.Skip";
    public const string RunbookHaltedEarlierStage =
        "Skipped because an earlier stage halted on a Critical/StopOnFailure violation";
    public static string RunbookUpstreamDependencyFailed(string? failedDependencyId) =>
        $"Skipped because upstream dependency '{failedDependencyId}' failed";
    public static string RuleEvaluationFailedFallback(string? ruleId) =>
        $"Rule '{ruleId}' evaluation failed.";
    public static string UnregisteredRuleEngineDescription(string engineName) =>
        $"Unregistered or unsupported quality rule engine '{engineName}'.";
    public static string EngineNotEnabledOrSupported(string engineName, string supported) =>
        $"Quality rule engine '{engineName}' is not enabled or supported. Supported engines: {supported}.";
    public static string EngineNotRegistered(string engineName, string registered) =>
        $"Quality rule engine '{engineName}' is not registered. Registered engines: {registered}.";
    public static string SkippedRulesCount(int count) => $"Skipped {count} rule(s)";
    public static string SkippedSuffix(int count) => $"; {count} skipped";
    public static string AdvisoryInfoSuffix(int count) => $"; {count} info notice(s)";
    public static string EvaluatedRulesZeroViolations(int count, string skipSuffix, string infoSuffix) =>
        $"Evaluated {count} rule(s) with 0 violations{skipSuffix}{infoSuffix}";
    public static string EvaluatedRulesWithViolations(int count, int violationCount, string skipSuffix, string infoSuffix) =>
        $"Evaluated {count} rule(s); {violationCount} violation(s){skipSuffix}{infoSuffix}";

    public const string CelDisplayName = "CEL Quality Engine";
    public static string CelDescription(int maxIterations) =>
        $"Evaluates Common Expression Language (CEL) quality rules with RFC3339 timestamps, comprehensions, ancestor context, automatic float epsilon tolerance, compiled program caching, and {maxIterations} max iterations.";
    public const string CelImplementationRequired = "CEL 'implementation' must be a non-empty expression string.";
    public static string InvalidCelSyntax(string exMessage) =>
        $"Invalid CEL expression syntax: {exMessage}";
    public static string CelExpressionEmpty(string? ruleId) =>
        $"Rule '{ruleId}' CEL expression is empty.";
    public static string CelEvaluationError(string? ruleId, object errVal) =>
        $"Rule '{ruleId}' CEL evaluation error: {errVal}";
    public static string CelMustReturnBool(string? ruleId, string typeName) =>
        $"Rule '{ruleId}' CEL expression must evaluate to a boolean, but returned '{typeName}'.";
    public static string CelEvaluationFailed(string? ruleId, string exMessage) =>
        $"Rule '{ruleId}' CEL evaluation failed: {exMessage}";

    public const string JsonLogicDisplayName = "JsonLogic Quality Engine";
    public const string JsonLogicDescription =
        "Evaluates structured JSON Logic quality rules with ISO-8601 comparison, .length property binding, !in operator, ancestor context, and automatic float epsilon tolerance.";
    public const string JsonLogicAssertionRequired = "'implementation' (or 'assert') logic condition is required.";
    public const string JsonLogicImplementationMustBeObject = "JsonLogic 'implementation' must be a JSON object expression.";
    public const string JsonLogicParseFailed = "Failed to parse JsonLogic assertion rule.";
    public static string InvalidJsonLogicSyntax(string exMessage) =>
        $"Invalid JsonLogic assertion syntax: {exMessage}";
    public static string RuleEvaluationFailed(string? ruleId, string exMessage) =>
        $"Rule '{ruleId}' evaluation failed: {exMessage}";
    public static string JsonLogicMaxDepthExceeded(int maxDepth) =>
        $"JsonLogic rule exceeds maximum nesting depth of {maxDepth}.";

    public const string UniqueDisplayName = "Unique Combination Engine";
    public const string UniqueDescription =
        "Evaluates O(N) composite uniqueness constraints across arrays with ancestor field support.";
    public const string UniqueRuleRequiresField =
        "Unique rule requires at least one field in 'fields' or 'implementation.fields'.";
    public static string UniqueRuleMissingFields(string? ruleId) =>
        $"Unique rule '{ruleId}' does not specify any fields.";
}
