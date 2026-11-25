using System.Globalization;
using System.Text.Json.Nodes;
using Bogus;

namespace EstateDataCollection.Tests.Common.Fuzzing;

internal static class NegativePayloadMutator
{
    public static void ApplyMutation(
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

    public static string CreateNonObjectJsonRootWithBogus(Faker faker)
    {
        return faker.Random.Int(0, 3) switch
        {
            0 => new JsonArray(faker.Lorem.Word(), faker.Random.Int(1, 999)).ToJsonString(),
            1 => JsonValue.Create(faker.Lorem.Sentence())!.ToJsonString(),
            2 => JsonValue.Create(faker.Random.Int(100, 999999))!.ToJsonString(),
            _ => JsonValue.Create(faker.Random.Bool())!.ToJsonString()
        };
    }

    public static string CreateMalformedJsonWithBogus(string validJson, Faker faker)
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
