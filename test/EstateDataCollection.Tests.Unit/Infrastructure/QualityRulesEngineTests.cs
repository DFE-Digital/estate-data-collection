using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Infrastructure.Validation.Engines;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class QualityRulesEngineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Paired_SchoolSite_SchemaJson_And_RulesJson_Should_Pass_Valid_Payload()
    {
        // Arrange
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rules = JsonSerializer.Deserialize<List<BusinessRule>>(EdcSchoolSiteContractBuilder.DefaultRulesJson, JsonOptions)!;
        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();
        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Act
        var errors = (await service.ValidatePayloadAsync(CdePayloadFixtures.ValidSchoolSitePayload, resolvedSchema)).ShouldSucceed();

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Paired_SchoolSite_SchemaJson_And_RulesJson_Should_Reject_Invalid_Timestamp()
    {
        // Arrange
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rules = JsonSerializer.Deserialize<List<BusinessRule>>(EdcSchoolSiteContractBuilder.DefaultRulesJson, JsonOptions)!;
        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();
        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Act
        var errors = (await service.ValidatePayloadAsync(CdePayloadFixtures.InvalidTimestampSchoolSitePayload, resolvedSchema)).ShouldSucceed();

        // Assert
        errors.Should().ContainSingle(e =>
            e.PropertyPath == "/survey" &&
            e.Message.Contains("Survey updated timestamp must be greater than or equal to survey creation timestamp"));
    }

    [Fact]
    public async Task Paired_Elements_SchemaJson_And_RulesJson_Should_Validate_Correctly()
    {
        // Arrange
        var elementsResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-survey-elements"))
            .WithSchemaJson(EdcSurveyElementsContractBuilder.DefaultSchemaJson)
            .WithRules(JsonSerializer.Deserialize<List<BusinessRule>>(EdcSurveyElementsContractBuilder.DefaultRulesJson, JsonOptions)!)
            .Build();
        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Act
        var errors = (await service.ValidatePayloadAsync(CdePayloadFixtures.ValidSurveyElementsPayload, elementsResolved)).ShouldSucceed();

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Paired_EvidenceAnswers_SchemaJson_And_RulesJson_Should_Validate_Correctly()
    {
        // Arrange
        var answersResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-survey-answers-evidence"))
            .WithSchemaJson(EdcEvidenceAnswersContractBuilder.DefaultSchemaJson)
            .WithRules(JsonSerializer.Deserialize<List<BusinessRule>>(EdcEvidenceAnswersContractBuilder.DefaultRulesJson, JsonOptions)!)
            .Build();
        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Act
        var errors = (await service.ValidatePayloadAsync(CdePayloadFixtures.ValidEvidenceAndAnswersPayload, answersResolved)).ShouldSucceed();

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task JsonLogicQualityEngine_Should_Preserve_Subsecond_Timestamp_Precision()
    {
        // Arrange
        var coordinator = new QualityRuleCoordinator();
        var timestampRuleJson = new QualityRulesBuilder()
            .AddCustomRule(
                "JL-SUBSECOND",
                "jsonlogic",
                "survey",
                "updated_at must be strictly greater than created_at",
                JsonNode.Parse("""{ ">": [ { "var": "updated_at" }, { "var": "created_at" } ] }""")!)
            .BuildJson();

        coordinator.ValidateRulesSyntax(timestampRuleJson, out var timestampRules).ShouldSucceed().Should().BeEmpty();
        const string payload = """
        {
          "survey": {
            "created_at": "2026-06-01T12:00:00.0000001Z",
            "updated_at": "2026-06-01T12:00:00.0000002Z"
          }
        }
        """;

        // Act
        var eval = (await coordinator.ValidateAsync(payload, timestampRules!)).ShouldSucceed();

        // Assert
        eval.IsValid.Should().BeTrue();
    }

    [Fact]
    public void JsonLogicQualityEngine_When_Ast_Exceeds_MaxDepth_Should_Report_SyntaxError()
    {
        // Arrange
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
        var engine = new JsonLogicQualityEngine();

        // Act
        var syntaxErrors = engine.ValidateSyntax(deepRule, "JL-DEEP");

        // Assert
        syntaxErrors.Should().ContainSingle(e =>
            e.Identifier == "JL-DEEP" &&
            e.Message.Contains("maximum nesting depth", StringComparison.OrdinalIgnoreCase));
    }
}
