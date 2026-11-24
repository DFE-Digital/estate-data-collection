using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Infrastructure.Validation.Engines;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Microsoft.Extensions.Options;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class QualityRulesEngineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Paired_SchoolSite_SchemaJson_And_RulesJson_Should_Pass_Valid_And_Reject_Invalid_Timestamp()
    {
        var schemaJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json");
        var rules = JsonSerializer.Deserialize<List<BusinessRule>>(
            CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.rules.json"), JsonOptions)!;

        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();

        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new JsonLogicValidator());

        var validErrors = (await service.ValidatePayloadAsync(CdePayloadFixtures.ValidSchoolSitePayload, resolvedSchema)).ShouldSucceed();
        validErrors.Should().BeEmpty();

        var invalidErrors = (await service.ValidatePayloadAsync(CdePayloadFixtures.InvalidTimestampSchoolSitePayload, resolvedSchema)).ShouldSucceed();
        invalidErrors.Should().ContainSingle(e =>
            e.PropertyPath == "/survey" &&
            e.Message.Contains("Survey updated timestamp must be greater than or equal to survey creation timestamp"));
    }

    [Fact]
    public async Task Paired_Elements_And_EvidenceAnswers_Schemas_And_Rules_Should_Validate_Correctly()
    {
        var elementsResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-survey-elements"))
            .WithSchemaJson(CdeContractFixtureLoader.LoadFile("schemas/edc-survey-elements.schema.json"))
            .WithRules(JsonSerializer.Deserialize<List<BusinessRule>>(
                CdeContractFixtureLoader.LoadFile("schemas/edc-survey-elements.rules.json"), JsonOptions)!)
            .Build();

        var answersResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-survey-answers-evidence"))
            .WithSchemaJson(CdeContractFixtureLoader.LoadFile("schemas/edc-survey-evidenceandanswers.schema.json"))
            .WithRules(JsonSerializer.Deserialize<List<BusinessRule>>(
                CdeContractFixtureLoader.LoadFile("schemas/edc-survey-evidenceandanswers.rules.json"), JsonOptions)!)
            .Build();

        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new JsonLogicValidator());

        (await service.ValidatePayloadAsync(CdePayloadFixtures.ValidSurveyElementsPayload, elementsResolved)).ShouldSucceed().Should().BeEmpty();
        (await service.ValidatePayloadAsync(CdePayloadFixtures.ValidEvidenceAndAnswersPayload, answersResolved)).ShouldSucceed().Should().BeEmpty();
    }

    [Fact]
    public async Task CelQualityEngine_Should_Validate_Timestamps_Nested_Arrays_And_Macros()
    {
        var celRulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-DQ1-TIMESTAMP", "survey", "CEL: Survey updated_at must be >= created_at using RFC3339 timestamp()",
                "timestamp(survey.updated_at) >= timestamp(survey.created_at)")
            .AddCelRule("CEL-DQ2-GRADE-D-REPAIRS", "elements[].occurrences[]", "CEL: Grade D occurrences must have at least one repair",
                "this.condition_grade != 'D' || size(this.repairs) > 0")
            .AddCelRule("CEL-DQ3-REPAIR-COST", "elements[].occurrences[].repairs[]", "CEL: Estimated cost must equal repair_quantity * repair_rate",
                "double(this.estimated_cost) == double(this.repair_quantity) * double(this.repair_rate)")
            .BuildJson();

        var validator = new JsonLogicValidator();
        var syntaxErrors = validator.ValidateRulesSyntax(celRulesJson, out var parsedRules).ShouldSucceed();
        syntaxErrors.Should().BeEmpty();
        parsedRules.Should().HaveCount(3);

        var validOutcome = (await validator.ValidateAsync(CdePayloadFixtures.ValidSurveyElementsPayload, parsedRules!)).ShouldSucceed();
        validOutcome.IsValid.Should().BeTrue();

        var mutated = JsonNode.Parse(CdePayloadFixtures.ValidSurveyElementsPayload)!;
        mutated["survey"]!["updated_at"] = "2026-03-20T00:00:00Z";
        mutated["elements"]![0]!["occurrences"]![0]!["repairs"] = new JsonArray();

        var invalidOutcome = (await validator.ValidateAsync(mutated.ToJsonString(), parsedRules!)).ShouldSucceed();
        invalidOutcome.IsValid.Should().BeFalse();
        invalidOutcome.Errors.Should().Contain(e => e.PropertyPath == "/survey" && e.Message.Contains("CEL: Survey updated_at must be >= created_at"));
        invalidOutcome.Errors.Should().Contain(e => e.PropertyPath == "/elements/0/occurrences/0" && e.Message.Contains("CEL: Grade D occurrences must have at least one repair"));
    }

    [Fact]
    public void CelQualityEngine_When_Expression_Has_Invalid_Syntax_Should_Report_Syntax_Error()
    {
        var invalidCelRulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-BROKEN", "survey", "Broken CEL syntax", "survey.updated_at >= == (")
            .BuildJson();

        var validator = new JsonLogicValidator();
        var syntaxErrors = validator.ValidateRulesSyntax(invalidCelRulesJson, out var parsedRules).ShouldSucceed();

        syntaxErrors.Should().ContainSingle(e => e.Identifier == "CEL-BROKEN" && e.Message.Contains("Invalid CEL expression syntax"));
        parsedRules.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_Engine_Should_Fail_By_Default_And_Skip_When_Configured()
    {
        var sqlRuleJson = new QualityRulesBuilder()
            .AddCustomRule("DQ-SQL-1", "sql", "survey", "Downstream SQL check",
                "SELECT COUNT(*) FROM survey WHERE updated_at < created_at")
            .BuildJson();

        var strictValidator = new JsonLogicValidator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            Options.Create(new ValidationOptions { UnknownEngineBehavior = UnknownEngineBehavior.Fail }));

        var lenientValidator = new JsonLogicValidator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            Options.Create(new ValidationOptions { UnknownEngineBehavior = UnknownEngineBehavior.Skip }));

        var strictSyntax = strictValidator.ValidateRulesSyntax(sqlRuleJson, out _).ShouldSucceed();
        strictSyntax.Should().ContainSingle(e =>
            e.Identifier == "DQ-SQL-1" &&
            e.Message.Contains("Quality rule engine 'sql' is not enabled or supported"));

        var lenientSyntax = lenientValidator.ValidateRulesSyntax(sqlRuleJson, out var parsedRules).ShouldSucceed();
        lenientSyntax.Should().BeEmpty();
        parsedRules.Should().NotBeNull();

        var runtimeStrict = (await strictValidator.ValidateAsync(CdePayloadFixtures.ValidSchoolSitePayload, parsedRules!)).ShouldSucceed();
        runtimeStrict.IsValid.Should().BeFalse();
        runtimeStrict.Errors.Should().ContainSingle(e => e.Message.Contains("engine 'sql' is not enabled or supported"));

        var runtimeLenient = (await lenientValidator.ValidateAsync(CdePayloadFixtures.ValidSchoolSitePayload, parsedRules!)).ShouldSucceed();
        runtimeLenient.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task JsonLogicQualityEngine_Should_Preserve_Subsecond_Timestamp_Precision_And_Enforce_Depth_Limit()
    {
        var validator = new JsonLogicValidator();

        // 1. Timestamps differing by 1 tick (100 nanoseconds) would collide if cast to double UtcTicks
        var timestampRuleJson = new QualityRulesBuilder()
            .AddCustomRule(
                "JL-SUBSECOND",
                "jsonlogic",
                "survey",
                "updated_at must be strictly greater than created_at",
                JsonNode.Parse("""{ ">": [ { "var": "updated_at" }, { "var": "created_at" } ] }""")!)
            .BuildJson();

        validator.ValidateRulesSyntax(timestampRuleJson, out var timestampRules).ShouldSucceed().Should().BeEmpty();
        const string payload = """
        {
          "survey": {
            "created_at": "2026-06-01T12:00:00.0000001Z",
            "updated_at": "2026-06-01T12:00:00.0000002Z"
          }
        }
        """;

        var eval = (await validator.ValidateAsync(payload, timestampRules!)).ShouldSucceed();
        eval.IsValid.Should().BeTrue();

        // 2. Deeply nested JsonLogic AST (> 64) should fail syntax validation cleanly
        JsonNode deepNode = JsonValue.Create(true)!;
        for (var i = 0; i < 70; i++)
        {
            deepNode = new JsonObject { ["!"] = deepNode };
        }

        var deepRule = new BusinessRule
        {
            Id = "JL-DEEP",
            Type = "custom",
            Engine = "jsonlogic",
            Path = "/",
            Description = "Excessively nested rule",
            Message = "Excessively nested rule",
            Implementation = deepNode
        };

        var syntaxErrors = new JsonLogicQualityEngine().ValidateSyntax(deepRule, "JL-DEEP");
        syntaxErrors.Should().ContainSingle(e =>
            e.Identifier == "JL-DEEP" &&
            e.Message.Contains("maximum nesting depth", StringComparison.OrdinalIgnoreCase));
    }
}
