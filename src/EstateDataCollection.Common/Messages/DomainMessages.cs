namespace EstateDataCollection.Common.Messages;

public static class DomainMessages
{
    public const string IdentifierTypeEmpty =
        "Identifier Type cannot be empty. Use 'None' to disable or provide an identifier type (e.g. 'URN', 'UKPRN', 'LAEstab').";
    public const string IdentifierTypeShortEmpty = "Identifier Type cannot be empty.";
    public const string IdentifierPointerForbiddenWhenNone =
        "Identifier JsonPointer must be empty when identifier Type is 'None'.";
    public const string IdentifierPointerShortForbiddenWhenNone =
        "JsonPointer cannot be set when identifier Type is 'None'.";
    public static string IdentifierTypeInvalidChars(string trimmedType) =>
        $"Identifier Type '{trimmedType}' must not contain whitespace, slashes, or '..'.";
    public static string IdentifierPointerInvalid(string pointer) =>
        $"Identifier JsonPointer '{pointer}' is not a valid JSON Pointer or dot-notation object path.";
    public static string IdentifierPointerInvalidWithExamples(string pointer) =>
        $"JsonPointer '{pointer}' is not a valid object property path (examples: '/establishment/id', '/school/urn').";
    public const string InvalidIdentifierConfigFallback = "Invalid identifier configuration.";
    public const string InvalidValidationPolicyFallback = "Invalid validation policy configuration.";

    public static string OnlyUnvalidatedCanBeSignedOff(object state) =>
        $"Only Unvalidated schemas can be signed off. Current state: '{state}'.";
    public static string SchemaStateCannotBeScheduledOrActivated(object state) =>
        $"Schema in state '{state}' cannot be scheduled or activated.";
    public const string EffectiveToBeforeEffectiveFrom = "EffectiveToUtc must be later than EffectiveFromUtc.";
    public static string SchemaStateCannotBeDeprecated(object state) =>
        $"Schema in state '{state}' cannot be deprecated.";
    public static string SchemaStateCannotBeRetired(object state) =>
        $"Schema in state '{state}' cannot be retired.";

    public static string UnsupportedFailOnMinimumSeverity(object value) =>
        $"Unsupported FailOnMinimumSeverity value '{value}'.";
    public static string UnsupportedCrossSubmissionDraftPolicy(object value) =>
        $"Unsupported CrossSubmissionDraftPolicy value '{value}'.";
    public const string NumericToleranceMustBeNonNegative = "NumericToleranceOverride must be non-negative.";

    public static string DraftPayloadMalformed(string errorMessage) =>
        $"Draft payload is not valid JSON: {errorMessage}";
    public static string IndexedDraftPayloadMalformed(int indexOneBased, string errorMessage) =>
        $"Draft payload #{indexOneBased} is not valid JSON: {errorMessage}";

    public static string IdentifierCannotBeEmpty(string effectiveType) =>
        $"Identifier '{effectiveType}' cannot be empty.";
    public static string IdentifierUnsafePartitionChars(string effectiveType, string trimmed) =>
        $"Identifier '{effectiveType}' value '{trimmed}' contains characters not permitted for storage partitioning ('/', '\\', or '..').";
    public static string IdentifierAtPathCannotBeEmpty(string effectiveType, string pointerPath) =>
        $"Identifier '{effectiveType}' at '{pointerPath}' cannot be empty.";
    public static string IdentifierMustBeScalar(string effectiveType, string pointerPath, object valueKind) =>
        $"Identifier '{effectiveType}' at '{pointerPath}' must be a scalar string or number, not {valueKind}.";

    public static string ConcurrentIdentifierIndexConflict(string blobName) =>
        $"Concurrent update conflict on identifier index '{blobName}'.";
    public const string SessionMetadataDeserialisationFailed = "Failed to deserialise session metadata.";
}
