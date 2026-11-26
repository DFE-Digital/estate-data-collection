namespace EstateDataCollection.Common.Messages;

public static class SchemaValidationMessages
{
    public const string OdcsYamlCannotBeEmpty = "ODCS YAML content cannot be empty.";
    public const string OdcsYamlDocumentEmpty = "ODCS YAML document was empty.";
    public static string OdcsYamlMalformedSyntax(string exMessage) =>
        $"Malformed ODCS YAML syntax: {exMessage}";
    public const string OdcsRootMustBeObject = "ODCS contract root must be a YAML mapping/object.";
    public const string OdcsMissingSchemaArray = "ODCS contract must contain a non-empty 'schema' array.";
    public const string OdcsTargetSchemaNotFound = "Could not resolve a target schema object in the ODCS contract.";
    public static string OdcsMaxNestingDepthExceeded(int maxDepth) =>
        $"ODCS schema nesting exceeds maximum depth of {maxDepth}.";
    public const string InvalidOdcsYamlFallback = "Invalid ODCS YAML.";
    public const string OdcsParseFailedFallback = "Failed to parse ODCS YAML contract.";

    public const string DraftBlobPathEmpty = "Draft blob path cannot be empty.";
    public const string MalformedDraftMetaJsonFallback = "Malformed draft .meta.json companion file.";
    public static string DraftMetaMustBeJsonObject(string metaBlobPath) =>
        $"Draft metadata file '{metaBlobPath}' must be a JSON object.";
    public static string DraftMetaMalformedJson(string metaBlobPath, string exMessage) =>
        $"Draft metadata file '{metaBlobPath}' is malformed JSON: {exMessage}";

    public const string SchemaNameAndPositiveVersionRequired = "SchemaName and positive Version are required.";
    public const string SignedOffByRequired = "signedOffBy is required to sign off an Unvalidated schema.";
    public const string SignedOffByIdentityRequired = "Signed-off-by identity is required.";
    public static string SchemaVersionInState(string name, int version, object state) =>
        $"Schema '{name}' v{version} is in '{state}' state.";
    public const string SchemaNameRequired = "Schema name is required.";
    public const string SchemaVersionMinimumOne = "Schema version must be greater than or equal to 1.";
    public const string InvalidSchemaIdentifier = "Invalid schema identifier.";
    public const string SchemaDeletedLogReason = "Schema version has been deleted.";
    public const string SchemaInvalidLogReason = "Schema is Invalid and cannot be scheduled.";
    public const string SchemaUnvalidatedLogReason = "Schema is Unvalidated and must be signed off before scheduling.";
    public const string SchemaRetiredLogReason = "Schema is already retired.";
    public static string UnsupportedLifecycleAction(string? lifecycleAction) =>
        $"Unsupported LifecycleAction '{lifecycleAction}'. Allowed values: Schedule, Activate, Deprecate, Retire.";
    public const string ScheduleRequiresInputField =
        "Either EffectiveFromUtc, LifecycleAction, IdentifierConfig, or ValidationPolicy must be provided.";
    public static string SchemaVerificationFoundErrors(int count) =>
        $"Schema verification found {count} error(s)";
    public const string SchemaVerifiedSuccessfully = "Schema verified successfully";

    public static string IdentifierPointerNotFound(string pointer) =>
        $"Identifier pointer '{pointer}' does not exist in the JSON Schema.";
    public static string IdentifierPointerMustBeScalar(string pointer) =>
        $"Identifier pointer '{pointer}' must resolve to a scalar string, integer, or number property.";
    public static string SchemaMissingDefaultIdentifierPath(string searchedFormatted) =>
        $"Schema does not declare a compatible establishment identifier property at any default path ({searchedFormatted}). Configure IdentifierConfig.JsonPointer (or upload a .meta.json), or set IdentifierConfig.Type to 'None'.";
    public static string IdentifierPointerInvalidSyntax(string rawPath) =>
        $"Identifier pointer '{rawPath}' is not a valid JSON Pointer or dot-notation path.";
    public static string PropertyNotDeclaredInSchema(string segment, string normalizedPointer) =>
        $"Property '{segment}' at path '{normalizedPointer}' is not declared in the JSON Schema.";
    public static string IdentifierEnumMustBeScalar(string normalizedPointer) =>
        $"Identifier enum at '{normalizedPointer}' must contain string or numeric values.";
    public static string IdentifierMustDeclareScalarType(string normalizedPointer) =>
        $"Identifier property at '{normalizedPointer}' must declare a scalar JSON Schema type ('string', 'integer', or 'number').";
    public static string IdentifierIncompatibleType(string normalizedPointer, string foundTypes) =>
        $"Identifier property at '{normalizedPointer}' has incompatible type '{foundTypes}'. Expected 'string', 'integer', or 'number'.";

    public const string IdentifierExtractorDisplayName = "Submission Identifier Extractor";
    public const string IdentifierExtractorDescription =
        "Extracts and validates configured establishment identifiers against schema requiredness and storage key safety constraints.";
    public static string IdentifierExtractionFailed(string identifierType) =>
        $"Failed to extract identifier '{identifierType}'.";
    public static string RequiredIdentifierMissingAtPath(string identifierType, string pointer) =>
        $"Required identifier '{identifierType}' was missing or null at '{pointer}'.";

    public const string SchemaValidatorFallbackDisplayName = "JSON Schema Validator";
    public const string SchemaValidatorFallbackDescription = "Structural JSON Schema validator.";
    public const string SchemaOrRulesRequired = "Either Schema JSON/YAML or Rules JSON must be provided.";
    public const string SchemaJsonEmptyOrNull = "Schema JSON was empty or null.";
    public const string MalformedJsonSchemaSyntax = "Malformed JSON Schema syntax";
    public static string MetaSchemaNotAllowed(string schemaUri) =>
        $"Meta-schema '{schemaUri}' is not in the configured AllowedMetaSchemas list.";

    public static string FixtureRejectedBySchema(string fixtureId) =>
        $"{fixtureId} rejected by schema.";
    public static string FixtureExpectedToPassRejected(string fixtureId) =>
        $"{fixtureId} was expected to pass, but was rejected by the schema.";
    public static string FixtureParseFailed(string fixtureId, string exMessage) =>
        $"{fixtureId} could not be parsed: {exMessage}";
    public static string FixtureQualityRuleEvalFailed(string fixtureId, string? description) =>
        $"{fixtureId} quality rule evaluation failed: {description}";
    public static string FixtureViolatedQualityRules(string fixtureId, string ruleErrors) =>
        $"{fixtureId} violated quality rules: {ruleErrors}";
    public static string FixtureRuleWarning(string fixtureId, string? ruleId, string message) =>
        $"{fixtureId} warning [{ruleId ?? "rule"}]: {message}";
    public static string FixtureFailedIdentifierExtraction(string fixtureId, string? errorMessage) =>
        $"{fixtureId} failed identifier extraction: {errorMessage}";
    public static string FixtureMissingRequiredIdentifier(string fixtureId, string effectiveIdentifierType, string expectedPath) =>
        $"{fixtureId} is missing required '{effectiveIdentifierType}' identifier at '{expectedPath}'.";
    public static string PayloadParseFailed(string exMessage) =>
        $"Could not parse payload: {exMessage}";
    public const string InvalidIdentifierFallback = "Invalid identifier.";
    public static string MissingRequiredIdentifierAtPath(string effectiveIdentifierType, string expectedPath) =>
        $"Missing required '{effectiveIdentifierType}' identifier at '{expectedPath}'.";
    public static string FixtureExpectedToFailPassed(string fixtureId) =>
        $"{fixtureId} was expected to fail, but passed the schema.";
    public static string FixtureUnmatchedExpectedErrors(string fixtureId, string unmatchedList, string actualSummary) =>
        $"{fixtureId} did not trigger expected error(s) [{unmatchedList}]. Actual triggered errors: {actualSummary}.";
    public static string FixtureWarningNoExpectedErrorsDetail(string actualSummary) =>
        $"Failed validation, but unknown if expected because no expectedErrors assertions were configured. Triggered: {actualSummary}.";
    public static string FixtureWarningNoExpectedErrors(string fixtureId, string actualSummary) =>
        $"{fixtureId} failed validation, but unknown if expected (no expectedErrors configured). Triggered error(s): {actualSummary}.";
    public const string PayloadRejectedByJsonSchemaFallback = "Payload rejected by JSON Schema.";
    public const string ValidationRejectedPayloadFallback = "validation rejected payload";
    public const string NoErrorsFallback = "none";

    public const string NoJsonSchemaProvided = "No JSON Schema provided";
    public const string MalformedJsonSchema = "Malformed JSON Schema";
    public static string SchemaErrorsCount(int count) => $"{count} schema error(s)";
    public const string SchemaValidPrefix = "Schema valid";
    public static string NoQualityRulesTargetingEngine(string engineId) =>
        $"No quality rules targeting '{engineId}'";
    public const string NoQualityRulesConfigured = "No quality rules configured";
    public const string QualityRuleEngineEvalFailed = "Quality rule engine evaluation failed";
    public static string RuleCountViolations(int ruleCount, int violationCount) =>
        $"{ruleCount} rule(s); {violationCount} violation(s)";
    public static string RuleCountVerified(int ruleCount) =>
        $"{ruleCount} rule(s) verified";
    public const string IdentifierExtractionDisabled = "Identifier extraction disabled by schema";
    public const string NoJsonSchemaForIdentifierAlignment = "No JSON Schema provided for identifier alignment";
    public static string IdentifierErrorsCount(int count) =>
        $"{count} identifier error(s)";
    public const string IdentifierConfigAlignedFallback = "Identifier config aligned";
    public static string IdentifierSummaryLabel(string effectiveType, string ptr, bool isRequired) =>
        $"Type '{effectiveType}' at '{ptr}' (required: {isRequired})";
    public const string RulesDidNotParseForConsistency = "Rules did not parse cleanly for consistency analysis";
    public static string ConsistencyErrorsCount(int count) =>
        $"{count} contract consistency error(s)";
    public static string ConsistencyCheckedWithWarnings(int itemsEvaluated, int warningCount) =>
        $"Checked {itemsEvaluated} rule(s); {warningCount} warning(s)";
    public static string ConsistencyCheckedClean(int itemsEvaluated) =>
        $"Checked {itemsEvaluated} rule(s); consistent with JSON Schema and DAG";
    public static string FixtureCheckSummary(string prefix, int validCount, int invalidCount, int invalidTriggers) =>
        invalidTriggers > 0
            ? $"{prefix}; checked {validCount} valid and {invalidCount} invalid fixture(s) (triggered {invalidTriggers} invalid fixture(s))"
            : $"{prefix}; checked {validCount} valid and {invalidCount} invalid fixture(s)";
}
