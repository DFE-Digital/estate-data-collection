using System.Text.Json.Nodes;
using Bogus;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Fixtures;

namespace EstateDataCollection.Tests.Common.Fuzzing;

public static class BogusNegativePayloadSuite
{
    private const string CustomLaEstabCelSchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "additionalProperties": false,
      "required": ["establishment", "survey", "summary"],
      "properties": {
        "establishment": {
          "type": "object",
          "additionalProperties": false,
          "required": ["laestab", "name", "pupil_count"],
          "properties": {
            "laestab": { "type": "string", "pattern": "^[0-9]{3}-[0-9]{4}$" },
            "name": { "type": "string", "minLength": 1 },
            "pupil_count": { "type": "integer", "minimum": 0 },
            "contact_email": { "type": ["string", "null"], "format": "email" }
          }
        },
        "survey": {
          "type": "object",
          "additionalProperties": false,
          "required": ["created_at", "updated_at"],
          "properties": {
            "created_at": { "type": "string", "format": "date-time" },
            "updated_at": { "type": "string", "format": "date-time" }
          }
        },
        "summary": {
          "type": "object",
          "additionalProperties": false,
          "required": ["total_checks", "open_issues"],
          "properties": {
            "total_checks": { "type": "integer", "minimum": 0 },
            "open_issues": { "type": "integer", "minimum": 0 }
          }
        }
      }
    }
    """;

    private static readonly string CustomLaEstabCelRulesJson = new QualityRulesBuilder()
        .AddJsonLogicTimestampRule("CEL-SUITE-TS-1")
        .AddCelRule(
            "CEL-SUITE-ISSUES-1",
            "summary",
            "Open issues cannot exceed total checks.",
            "open_issues <= total_checks")
        .AddCelRule(
            "CEL-SUITE-PUPILS-1",
            "establishment",
            "Establishment pupil count must be greater than zero.",
            "pupil_count > 0")
        .BuildJson();

    public static FuzzSchemaContract GetContract(FuzzSchemaProfile profile)
    {
        return profile switch
        {
            FuzzSchemaProfile.ValidV1 => new FuzzSchemaContract(
                Profile: profile,
                SchemaNamePrefix: "bogus_v1",
                SchemaJson: JsonSchemaFixtures.ValidV1Schema,
                RulesJson: null,
                IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true }),

            FuzzSchemaProfile.EdcSchoolSite => new FuzzSchemaContract(
                Profile: profile,
                SchemaNamePrefix: "bogus_sites",
                SchemaJson: EdcSchoolSiteContractBuilder.DefaultSchemaJson,
                RulesJson: EdcSchoolSiteContractBuilder.DefaultRulesJson,
                IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true }),

            FuzzSchemaProfile.EdcSurveyElements => new FuzzSchemaContract(
                Profile: profile,
                SchemaNamePrefix: "bogus_elements",
                SchemaJson: EdcSurveyElementsContractBuilder.DefaultSchemaJson,
                RulesJson: EdcSurveyElementsContractBuilder.DefaultRulesJson,
                IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true }),

            FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers => new FuzzSchemaContract(
                Profile: profile,
                SchemaNamePrefix: "bogus_evid_ans",
                SchemaJson: EdcEvidenceAnswersContractBuilder.DefaultSchemaJson,
                RulesJson: EdcEvidenceAnswersContractBuilder.DefaultRulesJson,
                IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true }),

            FuzzSchemaProfile.CustomLaEstabCel => new FuzzSchemaContract(
                Profile: profile,
                SchemaNamePrefix: "bogus_laestab_cel",
                SchemaJson: CustomLaEstabCelSchemaJson,
                RulesJson: CustomLaEstabCelRulesJson,
                IdentifierConfig: new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab", Required = true }),

            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };
    }

    public static JsonObject GenerateValidObject(FuzzSchemaProfile profile, int seed)
    {
        var faker = CreateFaker(seed);
        return FuzzPayloadGenerator.GenerateValidObject(profile, faker);
    }

    public static string GenerateValidJson(FuzzSchemaProfile profile, int seed)
    {
        return GenerateValidObject(profile, seed).ToJsonString();
    }

    public static IReadOnlyList<NegativeMutationKind> GetSupportedMutations(FuzzSchemaProfile profile)
    {
        return profile switch
        {
            FuzzSchemaProfile.ValidV1 =>
            [
                NegativeMutationKind.MissingRequiredRootProperty,
                NegativeMutationKind.NullInNonNullableField,
                NegativeMutationKind.WrongTypeStringForNumericOrBool,
                NegativeMutationKind.WrongTypeNumberForStringOrObject,
                NegativeMutationKind.AdditionalPropertyDisallowed,
                NegativeMutationKind.MissingOrBlankIdentifier,
                NegativeMutationKind.AdversarialStringInTypedField,
                NegativeMutationKind.NonObjectJsonRoot,
                NegativeMutationKind.MalformedJsonSyntax
            ],

            FuzzSchemaProfile.EdcSchoolSite =>
            [
                NegativeMutationKind.MissingRequiredRootProperty,
                NegativeMutationKind.MissingRequiredNestedProperty,
                NegativeMutationKind.NullInNonNullableField,
                NegativeMutationKind.WrongTypeStringForNumericOrBool,
                NegativeMutationKind.WrongTypeNumberForStringOrObject,
                NegativeMutationKind.WrongTypeObjectForArray,
                NegativeMutationKind.PatternViolation,
                NegativeMutationKind.InvalidEmailFormat,
                NegativeMutationKind.InvalidDateTimeFormat,
                NegativeMutationKind.JsonLogicQualityRuleViolation,
                NegativeMutationKind.MissingOrBlankIdentifier,
                NegativeMutationKind.AdversarialStringInTypedField,
                NegativeMutationKind.NonObjectJsonRoot,
                NegativeMutationKind.MalformedJsonSyntax
            ],

            FuzzSchemaProfile.EdcSurveyElements =>
            [
                NegativeMutationKind.MissingRequiredRootProperty,
                NegativeMutationKind.MissingRequiredNestedProperty,
                NegativeMutationKind.NullInNonNullableField,
                NegativeMutationKind.WrongTypeStringForNumericOrBool,
                NegativeMutationKind.WrongTypeNumberForStringOrObject,
                NegativeMutationKind.WrongTypeObjectForArray,
                NegativeMutationKind.PatternViolation,
                NegativeMutationKind.InvalidEmailFormat,
                NegativeMutationKind.InvalidDateTimeFormat,
                NegativeMutationKind.InvalidEnumValue,
                NegativeMutationKind.JsonLogicQualityRuleViolation,
                NegativeMutationKind.MissingOrBlankIdentifier,
                NegativeMutationKind.AdversarialStringInTypedField,
                NegativeMutationKind.NonObjectJsonRoot,
                NegativeMutationKind.MalformedJsonSyntax
            ],

            FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers =>
            [
                NegativeMutationKind.MissingRequiredRootProperty,
                NegativeMutationKind.MissingRequiredNestedProperty,
                NegativeMutationKind.NullInNonNullableField,
                NegativeMutationKind.WrongTypeStringForNumericOrBool,
                NegativeMutationKind.WrongTypeNumberForStringOrObject,
                NegativeMutationKind.WrongTypeObjectForArray,
                NegativeMutationKind.PatternViolation,
                NegativeMutationKind.InvalidEmailFormat,
                NegativeMutationKind.InvalidDateTimeFormat,
                NegativeMutationKind.InvalidEnumValue,
                NegativeMutationKind.JsonLogicQualityRuleViolation,
                NegativeMutationKind.MissingOrBlankIdentifier,
                NegativeMutationKind.AdversarialStringInTypedField,
                NegativeMutationKind.NonObjectJsonRoot,
                NegativeMutationKind.MalformedJsonSyntax
            ],

            FuzzSchemaProfile.CustomLaEstabCel =>
            [
                NegativeMutationKind.MissingRequiredRootProperty,
                NegativeMutationKind.MissingRequiredNestedProperty,
                NegativeMutationKind.NullInNonNullableField,
                NegativeMutationKind.WrongTypeStringForNumericOrBool,
                NegativeMutationKind.WrongTypeNumberForStringOrObject,
                NegativeMutationKind.PatternViolation,
                NegativeMutationKind.InvalidEmailFormat,
                NegativeMutationKind.InvalidDateTimeFormat,
                NegativeMutationKind.AdditionalPropertyDisallowed,
                NegativeMutationKind.JsonLogicQualityRuleViolation,
                NegativeMutationKind.CelQualityRuleViolation,
                NegativeMutationKind.MissingOrBlankIdentifier,
                NegativeMutationKind.AdversarialStringInTypedField,
                NegativeMutationKind.NonObjectJsonRoot,
                NegativeMutationKind.MalformedJsonSyntax
            ],

            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };
    }

    public static NegativePayloadCase GenerateNegativeCase(
        FuzzSchemaProfile profile,
        NegativeMutationKind mutationKind,
        int seed)
    {
        var faker = CreateFaker(seed * 31 + (int)mutationKind + 7);
        var root = GenerateValidObject(profile, seed);

        if (mutationKind == NegativeMutationKind.MalformedJsonSyntax)
        {
            var malformed = NegativePayloadMutator.CreateMalformedJsonWithBogus(root.ToJsonString(), faker);
            return new NegativePayloadCase(
                profile,
                mutationKind,
                seed,
                malformed,
                IsMalformedJson: true,
                Description: $"{profile} / {mutationKind} (seed {seed})");
        }

        if (mutationKind == NegativeMutationKind.NonObjectJsonRoot)
        {
            var nonObject = NegativePayloadMutator.CreateNonObjectJsonRootWithBogus(faker);
            return new NegativePayloadCase(
                profile,
                mutationKind,
                seed,
                nonObject,
                IsMalformedJson: false,
                Description: $"{profile} / {mutationKind} (seed {seed})");
        }

        NegativePayloadMutator.ApplyMutation(profile, mutationKind, root, faker);

        return new NegativePayloadCase(
            profile,
            mutationKind,
            seed,
            root.ToJsonString(),
            IsMalformedJson: false,
            Description: $"{profile} / {mutationKind} (seed {seed})");
    }

    public static IEnumerable<NegativePayloadCase> GenerateAllNegativeCases(params int[] seeds)
    {
        var effectiveSeeds = seeds.Length > 0 ? seeds : [101, 202];
        foreach (var profile in Enum.GetValues<FuzzSchemaProfile>())
        {
            foreach (var mutation in GetSupportedMutations(profile))
            {
                foreach (var seed in effectiveSeeds)
                {
                    yield return GenerateNegativeCase(profile, mutation, seed);
                }
            }
        }
    }

    private static Faker CreateFaker(int seed) => new("en_GB") { Random = new Randomizer(seed) };
}
