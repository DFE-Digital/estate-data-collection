using System.Globalization;
using System.Text.Json.Nodes;
using Bogus;

namespace EstateDataCollection.Tests.Common.Fuzzing;

internal static class FuzzPayloadGenerator
{
    public static JsonObject GenerateValidObject(FuzzSchemaProfile profile, Faker faker)
    {
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
}
