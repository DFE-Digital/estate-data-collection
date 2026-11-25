using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Tests.Common.Fuzzing;

public enum FuzzSchemaProfile
{
    ValidV1,
    EdcSchoolSite,
    EdcSurveyElements,
    EdcSurveyEvidenceAndAnswers,
    CustomLaEstabCel
}

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

public sealed record FuzzSchemaContract(
    FuzzSchemaProfile Profile,
    string SchemaNamePrefix,
    string SchemaJson,
    string? RulesJson,
    SchemaIdentifierConfig IdentifierConfig);

public sealed record NegativePayloadCase(
    FuzzSchemaProfile Profile,
    NegativeMutationKind MutationKind,
    int Seed,
    string PayloadJson,
    bool IsMalformedJson,
    string Description);
