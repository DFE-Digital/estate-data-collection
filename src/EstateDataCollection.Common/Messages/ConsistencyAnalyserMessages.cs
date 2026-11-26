namespace EstateDataCollection.Common.Messages;

public static class ConsistencyAnalyserMessages
{
    public const string DescriptorDisplayName = "Contract Consistency Analyser";
    public const string DescriptorDescription =
        "Static analyser checking rule-to-schema paths, field casing, enum literals, DependsOn DAG/stage integrity, severity threshold inversions, and required-vs-optional conflicts.";

    public static string RulePathCaseMismatch(string ruleId, string? rulePath, string? suggestedPath) =>
        $"Rule '{ruleId}' path '{rulePath}' does not match JSON Schema casing. Did you mean '{suggestedPath}'?";
    public static string RuleTargetPathMissing(string ruleId, string? rulePath, string? suggestedPath) =>
        $"Rule '{ruleId}' target path '{rulePath}' does not exist in JSON Schema.{(suggestedPath is not null ? $" Did you mean '{suggestedPath}'?" : string.Empty)}";

    public static string MissingDependency(string ruleId, string depId) =>
        $"Rule '{ruleId}' dependsOn '{depId}', which does not exist in the quality rules contract.";
    public static string LaterStageDependency(string ruleId, int ruleStage, string depId, int depStage) =>
        $"Rule '{ruleId}' (Stage {ruleStage}) cannot depend on '{depId}' in a later Stage ({depStage}).";
    public static string CircularDependencyCycle(string cycleText) =>
        $"Circular dependency cycle detected in 'dependsOn': {cycleText}.";

    public static string EnumLiteralMismatch(string ruleId, string varPath, string literalValue, string allowedValuesJoined, string? caseMatch) =>
        $"Rule '{ruleId}' compares field '{varPath}' against enum literal '{literalValue}', which is not in the JSON Schema enum [{allowedValuesJoined}].{(caseMatch is not null ? $" Did you mean '{caseMatch}'?" : string.Empty)}";

    public static string ThresholdSeverityInversion(
        string fieldName,
        object higherSev,
        string higherRuleId,
        string directionWord,
        object higherThreshold,
        object lowerSev,
        string lowerRuleId,
        object lowerThreshold) =>
        $"Threshold/severity inversion on '{fieldName}': {higherSev} rule '{higherRuleId}' enforces a stricter {directionWord} ({higherThreshold}) than {lowerSev} rule '{lowerRuleId}' ({lowerThreshold}), making the {lowerSev} rule unreachable.";

    public static string SchemaMinimumConflict(string ruleId, string fieldName, object threshold, object schemaMax) =>
        $"Rule '{ruleId}' requires '{fieldName}' >= {threshold}, which conflicts with JSON Schema maximum ({schemaMax}).";
    public static string SchemaMaximumConflict(string ruleId, string fieldName, object threshold, object schemaMin) =>
        $"Rule '{ruleId}' requires '{fieldName}' <= {threshold}, which conflicts with JSON Schema minimum ({schemaMin}).";

    public static string RequiredInSchemaVsConditionalOptional(string fieldName, string ruleId) =>
        $"Field '{fieldName}' is unconditionally marked 'required' in JSON Schema, conflicting with conditional-optional exemption in rule '{ruleId}'.";
    public static string RequiredByRuleVsConditionalOptional(string fieldName, string reqRuleId, string ruleId) =>
        $"Field '{fieldName}' is unconditionally required by rule '{reqRuleId}', conflicting with conditional-optional exemption in rule '{ruleId}'.";

    public static string FieldCasingMismatch(string ruleId, string exprToken, string? suggestedPath) =>
        $"Rule '{ruleId}' references '{exprToken}', which has mismatched casing against JSON Schema. Did you mean '{suggestedPath}'?";
    public static string FieldMissingInSchema(string ruleId, string exprToken, string? suggestedPath) =>
        $"Rule '{ruleId}' references field '{exprToken}', which does not exist in JSON Schema.{(suggestedPath is not null ? $" Did you mean '{suggestedPath}'?" : string.Empty)}";
}
