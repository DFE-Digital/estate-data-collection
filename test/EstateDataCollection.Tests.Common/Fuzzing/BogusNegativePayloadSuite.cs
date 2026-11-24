using System.Globalization;
using System.Text.Json.Nodes;
using Bogus;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;

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
                SchemaJson: CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json"),
                RulesJson: CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.rules.json"),
                IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true }),

            FuzzSchemaProfile.EdcSurveyElements => new FuzzSchemaContract(
                Profile: profile,
                SchemaNamePrefix: "bogus_elements",
                SchemaJson: CdeContractFixtureLoader.LoadFile("schemas/edc-survey-elements.schema.json"),
                RulesJson: CdeContractFixtureLoader.LoadFile("schemas/edc-survey-elements.rules.json"),
                IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true }),

            FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers => new FuzzSchemaContract(
                Profile: profile,
                SchemaNamePrefix: "bogus_evid_ans",
                SchemaJson: CdeContractFixtureLoader.LoadFile("schemas/edc-survey-evidenceandanswers.schema.json"),
                RulesJson: CdeContractFixtureLoader.LoadFile("schemas/edc-survey-evidenceandanswers.rules.json"),
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

        return profile switch
        {
            FuzzSchemaProfile.ValidV1 => new JsonObject
            {
                ["urn"] = faker.Random.Int(100000, 999999),
                ["school_name"] = $"{faker.Address.City()} {faker.PickRandom("Academy", "Primary School", "High School")}",
                ["school_address"] = faker.Address.FullAddress().Replace('\n', ','),
                ["school_capacity"] = faker.Random.Int(50, 2200)
            },

            FuzzSchemaProfile.EdcSchoolSite => BuildSchoolSiteObject(faker),
            FuzzSchemaProfile.EdcSurveyElements => BuildSurveyElementsObject(faker),
            FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers => BuildEvidenceAndAnswersObject(faker),
            FuzzSchemaProfile.CustomLaEstabCel => BuildCustomLaEstabObject(faker),

            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };
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
            var malformed = CreateMalformedJsonWithBogus(root.ToJsonString(), faker);
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
            var nonObject = CreateNonObjectJsonRootWithBogus(faker);
            return new NegativePayloadCase(
                profile,
                mutationKind,
                seed,
                nonObject,
                IsMalformedJson: false,
                Description: $"{profile} / {mutationKind} (seed {seed})");
        }

        ApplyMutation(profile, mutationKind, root, faker);

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

    private static Faker CreateFaker(int seed)
    {
        return new Faker("en_GB") { Random = new Randomizer(seed) };
    }

    private static JsonObject BuildSurveyObject(Faker faker, out DateTimeOffset createdAt, out DateTimeOffset updatedAt)
    {
        createdAt = new DateTimeOffset(faker.Date.Between(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)), TimeSpan.Zero);
        updatedAt = createdAt.AddMinutes(faker.Random.Int(0, 720));

        return new JsonObject
        {
            ["id"] = $"SURV-{faker.Random.AlphaNumeric(8).ToUpperInvariant()}",
            ["survey_name"] = $"CDC2 {faker.Company.CatchPhrase()}",
            ["participant_id"] = $"PART-{faker.Random.Int(100, 9999)}",
            ["surveyor_email"] = faker.Internet.Email(),
            ["is_complete"] = faker.Random.Bool(),
            ["created_at"] = createdAt.ToString("O", CultureInfo.InvariantCulture),
            ["updated_at"] = updatedAt.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static JsonObject BuildSchoolSiteObject(Faker faker)
    {
        var survey = BuildSurveyObject(faker, out _, out _);
        var school = new JsonObject
        {
            ["urn"] = faker.Random.Int(100000, 999999).ToString(CultureInfo.InvariantCulture),
            ["name"] = $"{faker.Address.City()} Academy",
            ["local_authority"] = faker.Address.County()
        };

        var floors = new JsonArray
        {
            new JsonObject { ["name"] = faker.PickRandom("Ground Floor", "First Floor", "Basement") }
        };

        var blocks = new JsonArray
        {
            new JsonObject
            {
                ["name"] = $"Block {faker.Random.Char('A', 'Z')}",
                ["type"] = faker.PickRandom("Teaching", "Admin", "Sports", "Catering"),
                ["floors"] = floors
            }
        };

        var sites = new JsonArray
        {
            new JsonObject
            {
                ["name"] = $"{faker.Address.StreetName()} Campus",
                ["blocks"] = blocks
            }
        };

        return new JsonObject
        {
            ["survey"] = survey,
            ["school"] = school,
            ["sites"] = sites
        };
    }

    private static JsonObject BuildSurveyElementsObject(Faker faker)
    {
        var survey = BuildSurveyObject(faker, out _, out _);
        var school = new JsonObject
        {
            ["urn"] = faker.Random.Int(100000, 999999).ToString(CultureInfo.InvariantCulture),
            ["name"] = $"{faker.Address.City()} Primary"
        };

        var repairs = new JsonArray
        {
            new JsonObject
            {
                ["id"] = $"REP-{faker.Random.Int(100, 9999)}",
                ["repair_description"] = faker.Lorem.Sentence(4),
                ["repair_item"] = faker.Commerce.ProductMaterial(),
                ["repair_classification"] = faker.PickRandom("X", "Y", "Z"),
                ["uom"] = faker.PickRandom("m2", "nr", "m"),
                ["repair_quantity"] = Math.Round(faker.Random.Double(1, 250), 2),
                ["repair_rate"] = Math.Round(faker.Random.Double(10, 500), 2),
                ["estimated_cost"] = Math.Round(faker.Random.Double(100, 50000), 2)
            }
        };

        var occurrences = new JsonArray
        {
            new JsonObject
            {
                ["id"] = $"OCC-{faker.Random.Int(100, 9999)}",
                ["name"] = $"{faker.Commerce.ProductAdjective()} Roof",
                ["basis_of_assessment"] = faker.PickRandom("Seen", "Not Seen", "Assumed"),
                ["condition_grade"] = faker.PickRandom("A", "B", "C", "D"),
                ["condition_description"] = faker.Lorem.Sentence(5),
                ["replacement"] = new JsonObject
                {
                    ["rem_life"] = faker.Random.Int(1, 30),
                    ["uom"] = "m2",
                    ["replacement_quantity"] = Math.Round(faker.Random.Double(10, 500), 1),
                    ["replacement_rate"] = Math.Round(faker.Random.Double(20, 400), 1)
                },
                ["repairs"] = repairs
            }
        };

        var elements = new JsonArray
        {
            new JsonObject
            {
                ["id"] = $"ELM-{faker.Random.Int(100, 9999)}",
                ["NRM3_code"] = $"{faker.Random.Int(1, 9)}.{faker.Random.Int(1, 9)}.{faker.Random.Int(1, 9)}",
                ["description"] = faker.Lorem.Sentence(3),
                ["asset_id"] = $"Block {faker.Random.Char('A', 'H')}",
                ["asset_type"] = faker.PickRandom("site", "block", "floor", "space"),
                ["occurrences"] = occurrences
            }
        };

        return new JsonObject
        {
            ["survey"] = survey,
            ["school"] = school,
            ["elements"] = elements
        };
    }

    private static JsonObject BuildEvidenceAndAnswersObject(Faker faker)
    {
        var survey = BuildSurveyObject(faker, out var createdAt, out _);
        var school = new JsonObject
        {
            ["urn"] = faker.Random.Int(100000, 999999).ToString(CultureInfo.InvariantCulture),
            ["name"] = $"{faker.Address.City()} College"
        };

        var evidenceId = $"EVD-{faker.Random.Int(100, 9999)}";
        var answers = new JsonArray
        {
            new JsonObject
            {
                ["id"] = $"ANS-{faker.Random.Int(100, 9999)}",
                ["question_id"] = $"Q-{faker.Random.AlphaNumeric(5).ToUpperInvariant()}",
                ["asset_id"] = $"Block {faker.Random.Char('A', 'H')}",
                ["asset_type"] = faker.PickRandom("site", "block", "floor", "space", "element"),
                ["value"] = faker.Random.Bool().ToString().ToLowerInvariant(),
                ["value_type"] = faker.PickRandom("text", "number", "boolean", "lookup_list", "date"),
                ["created_at"] = createdAt.ToString("O", CultureInfo.InvariantCulture),
                ["updated_at"] = createdAt.AddMinutes(5).ToString("O", CultureInfo.InvariantCulture),
                ["evidence"] = new JsonArray
                {
                    new JsonObject { ["id"] = evidenceId }
                }
            }
        };

        var evidence = new JsonArray
        {
            new JsonObject
            {
                ["id"] = evidenceId,
                ["file_name"] = faker.System.FileName("jpg"),
                ["file_path"] = $"/evidence/{createdAt.Year}/{faker.System.FileName("jpg")}",
                ["mime_type"] = "image/jpeg"
            }
        };

        return new JsonObject
        {
            ["survey"] = survey,
            ["school"] = school,
            ["answers"] = answers,
            ["evidence"] = evidence
        };
    }

    private static JsonObject BuildCustomLaEstabObject(Faker faker)
    {
        var createdAt = new DateTimeOffset(faker.Date.Between(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)), TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(faker.Random.Int(1, 600));
        var totalChecks = faker.Random.Int(5, 50);
        var openIssues = faker.Random.Int(0, totalChecks);

        return new JsonObject
        {
            ["establishment"] = new JsonObject
            {
                ["laestab"] = $"{faker.Random.Int(100, 999)}-{faker.Random.Int(1000, 9999)}",
                ["name"] = $"{faker.Address.City()} Grammar School",
                ["pupil_count"] = faker.Random.Int(10, 2000),
                ["contact_email"] = faker.Internet.Email()
            },
            ["survey"] = new JsonObject
            {
                ["created_at"] = createdAt.ToString("O", CultureInfo.InvariantCulture),
                ["updated_at"] = updatedAt.ToString("O", CultureInfo.InvariantCulture)
            },
            ["summary"] = new JsonObject
            {
                ["total_checks"] = totalChecks,
                ["open_issues"] = openIssues
            }
        };
    }

    private static void ApplyMutation(
        FuzzSchemaProfile profile,
        NegativeMutationKind mutationKind,
        JsonObject root,
        Faker faker)
    {
        switch (mutationKind)
        {
            case NegativeMutationKind.MissingRequiredRootProperty:
                var rootKeys = profile switch
                {
                    FuzzSchemaProfile.ValidV1 => new[] { "urn", "school_name", "school_address" },
                    FuzzSchemaProfile.EdcSchoolSite => ["survey", "school", "sites"],
                    FuzzSchemaProfile.EdcSurveyElements => ["survey", "school", "elements"],
                    FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers => ["survey", "school", "answers", "evidence"],
                    FuzzSchemaProfile.CustomLaEstabCel => ["establishment", "survey", "summary"],
                    _ => throw new ArgumentOutOfRangeException(nameof(profile))
                };
                root.Remove(faker.PickRandom(rootKeys));
                break;

            case NegativeMutationKind.MissingRequiredNestedProperty:
                switch (profile)
                {
                    case FuzzSchemaProfile.EdcSchoolSite:
                        root["sites"]!.AsArray()[0]!.AsObject().Remove("blocks");
                        break;
                    case FuzzSchemaProfile.EdcSurveyElements:
                        root["elements"]!.AsArray()[0]!.AsObject().Remove("NRM3_code");
                        break;
                    case FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers:
                        root["answers"]!.AsArray()[0]!.AsObject().Remove("question_id");
                        break;
                    case FuzzSchemaProfile.CustomLaEstabCel:
                        root["establishment"]!.AsObject().Remove("pupil_count");
                        break;
                }
                break;

            case NegativeMutationKind.NullInNonNullableField:
                switch (profile)
                {
                    case FuzzSchemaProfile.ValidV1:
                        root["school_name"] = null;
                        break;
                    case FuzzSchemaProfile.EdcSchoolSite:
                    case FuzzSchemaProfile.EdcSurveyElements:
                    case FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers:
                        root["survey"]!.AsObject()["survey_name"] = null;
                        break;
                    case FuzzSchemaProfile.CustomLaEstabCel:
                        root["establishment"]!.AsObject()["name"] = null;
                        break;
                }
                break;

            case NegativeMutationKind.WrongTypeStringForNumericOrBool:
                switch (profile)
                {
                    case FuzzSchemaProfile.ValidV1:
                        root["urn"] = faker.Lorem.Word();
                        break;
                    case FuzzSchemaProfile.EdcSchoolSite:
                    case FuzzSchemaProfile.EdcSurveyElements:
                    case FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers:
                        root["survey"]!.AsObject()["is_complete"] = faker.PickRandom("yes", "no", "maybe");
                        break;
                    case FuzzSchemaProfile.CustomLaEstabCel:
                        root["establishment"]!.AsObject()["pupil_count"] = faker.Lorem.Word();
                        break;
                }
                break;

            case NegativeMutationKind.WrongTypeNumberForStringOrObject:
                switch (profile)
                {
                    case FuzzSchemaProfile.ValidV1:
                        root["school_address"] = faker.Random.Int(100, 9999);
                        break;
                    case FuzzSchemaProfile.EdcSchoolSite:
                    case FuzzSchemaProfile.EdcSurveyElements:
                    case FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers:
                        root["school"]!.AsObject()["urn"] = faker.Random.Int(100000, 999999);
                        break;
                    case FuzzSchemaProfile.CustomLaEstabCel:
                        root["establishment"]!.AsObject()["name"] = faker.Random.Double(1, 500);
                        break;
                }
                break;

            case NegativeMutationKind.WrongTypeObjectForArray:
                switch (profile)
                {
                    case FuzzSchemaProfile.EdcSchoolSite:
                        root["sites"] = new JsonObject { ["bogus"] = faker.Lorem.Word() };
                        break;
                    case FuzzSchemaProfile.EdcSurveyElements:
                        root["elements"] = new JsonObject { ["bogus"] = faker.Lorem.Word() };
                        break;
                    case FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers:
                        root["answers"] = new JsonObject { ["bogus"] = faker.Lorem.Word() };
                        break;
                }
                break;

            case NegativeMutationKind.PatternViolation:
                switch (profile)
                {
                    case FuzzSchemaProfile.EdcSchoolSite:
                        root["school"]!.AsObject()["urn"] = $"BAD-{faker.Lorem.Word()}";
                        break;
                    case FuzzSchemaProfile.EdcSurveyElements:
                        root["elements"]!.AsArray()[0]!.AsObject()["id"] = $"WRONG-{faker.Lorem.Word()}";
                        break;
                    case FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers:
                        root["answers"]!.AsArray()[0]!.AsObject()["id"] = $"INVALID-{faker.Lorem.Word()}";
                        break;
                    case FuzzSchemaProfile.CustomLaEstabCel:
                        root["establishment"]!.AsObject()["laestab"] = faker.Lorem.Slug();
                        break;
                }
                break;

            case NegativeMutationKind.InvalidEmailFormat:
                var badEmail = $"{faker.Lorem.Word()}_invalid@@{faker.Lorem.Word()}";
                if (profile == FuzzSchemaProfile.CustomLaEstabCel)
                {
                    root["establishment"]!.AsObject()["contact_email"] = badEmail;
                }
                else
                {
                    root["survey"]!.AsObject()["surveyor_email"] = badEmail;
                }
                break;

            case NegativeMutationKind.InvalidDateTimeFormat:
                root["survey"]!.AsObject()["updated_at"] = $"not-a-timestamp-{faker.Lorem.Word()}";
                break;

            case NegativeMutationKind.InvalidEnumValue:
                var bogusEnum = $"INVALID_{faker.Lorem.Word().ToUpperInvariant()}";
                if (profile == FuzzSchemaProfile.EdcSurveyElements)
                {
                    root["elements"]!.AsArray()[0]!.AsObject()["asset_type"] = bogusEnum;
                }
                else if (profile == FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers)
                {
                    root["answers"]!.AsArray()[0]!.AsObject()["value_type"] = bogusEnum;
                }
                break;

            case NegativeMutationKind.AdditionalPropertyDisallowed:
                root[$"unexpected_{faker.Database.Column()}"] = faker.Lorem.Sentence();
                break;

            case NegativeMutationKind.JsonLogicQualityRuleViolation:
            {
                var survey = root["survey"]!.AsObject();
                var created = DateTimeOffset.Parse(survey["created_at"]!.GetValue<string>(), CultureInfo.InvariantCulture);
                var earlierUpdated = created.AddMinutes(-faker.Random.Int(1, 600));
                survey["updated_at"] = earlierUpdated.ToString("O", CultureInfo.InvariantCulture);
                break;
            }

            case NegativeMutationKind.CelQualityRuleViolation:
            {
                var summary = root["summary"]!.AsObject();
                var totalChecks = summary["total_checks"]!.GetValue<int>();
                summary["open_issues"] = totalChecks + faker.Random.Int(1, 25);
                break;
            }

            case NegativeMutationKind.MissingOrBlankIdentifier:
                switch (profile)
                {
                    case FuzzSchemaProfile.ValidV1:
                        root.Remove("urn");
                        break;
                    case FuzzSchemaProfile.EdcSchoolSite:
                    case FuzzSchemaProfile.EdcSurveyElements:
                    case FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers:
                        root["school"]!.AsObject().Remove("urn");
                        break;
                    case FuzzSchemaProfile.CustomLaEstabCel:
                        root["establishment"]!.AsObject()["laestab"] = "   ";
                        break;
                }
                break;

            case NegativeMutationKind.AdversarialStringInTypedField:
            {
                var adversarial = faker.PickRandom(
                    "🏫🔥'; DROP TABLE submissions;--",
                    "<script>alert('xss')</script>",
                    "{\"nested\":\"json\"} \u0000 \t \n",
                    faker.Lorem.Paragraphs(2));

                switch (profile)
                {
                    case FuzzSchemaProfile.ValidV1:
                        root["urn"] = adversarial;
                        break;
                    case FuzzSchemaProfile.EdcSchoolSite:
                    case FuzzSchemaProfile.EdcSurveyElements:
                    case FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers:
                        root["survey"]!.AsObject()["surveyor_email"] = adversarial;
                        break;
                    case FuzzSchemaProfile.CustomLaEstabCel:
                        root["establishment"]!.AsObject()["contact_email"] = adversarial;
                        break;
                }
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(mutationKind), mutationKind, null);
        }
    }

    private static string CreateNonObjectJsonRootWithBogus(Faker faker)
    {
        return faker.Random.Int(0, 3) switch
        {
            0 => new JsonArray(faker.Lorem.Word(), faker.Random.Int(1, 999)).ToJsonString(),
            1 => JsonValue.Create(faker.Lorem.Sentence())!.ToJsonString(),
            2 => JsonValue.Create(faker.Random.Int(100, 999999))!.ToJsonString(),
            _ => JsonValue.Create(faker.Random.Bool())!.ToJsonString()
        };
    }

    private static string CreateMalformedJsonWithBogus(string validJson, Faker faker)
    {
        return faker.Random.Int(0, 3) switch
        {
            0 => validJson[..(validJson.Length / 2)],
            1 => $"{validJson}, \"trailing\": {faker.Lorem.Word()}",
            2 => $"{{ \"urn\": {faker.Random.Int(100, 999)}, {faker.Lorem.Word()}: unquoted }}",
            _ => $"not-json-at-all: {faker.Lorem.Sentence()}"
        };
    }
}
