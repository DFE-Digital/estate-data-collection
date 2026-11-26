namespace EstateDataCollection.Tests.Common.Fuzzing;

public enum NegativeMutationKind
{
    MissingRequiredRootProperty,
    MissingRequiredNestedProperty,
    NullInNonNullableField,
    WrongTypeStringForNumericOrBool,
    WrongTypeNumberForStringOrObject,
    WrongTypeObjectForArray,
    PatternViolation,
    InvalidEmailFormat,
    InvalidDateTimeFormat,
    InvalidEnumValue,
    AdditionalPropertyDisallowed,
    JsonLogicQualityRuleViolation,
    CelQualityRuleViolation,
    MissingOrBlankIdentifier,
    AdversarialStringInTypedField,
    NonObjectJsonRoot,
    MalformedJsonSyntax
}