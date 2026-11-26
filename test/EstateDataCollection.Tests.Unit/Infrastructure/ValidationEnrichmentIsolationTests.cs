using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Infrastructure.Workflow.Engines;
using EstateDataCollection.Infrastructure.Workflow.Enrichment;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class ValidationEnrichmentIsolationTests
{
    private const string EnrichmentTestPayload = """
    {
      "sites": [{ "id": "SITE-1", "name": "North Campus" }],
      "answers": [{ "site_ref": "SITE-1", "value": "42" }]
    }
    """;

    private static IReadOnlyList<BusinessRule> BuildStandardRules() => new QualityRulesBuilder()
        .AddCustomRule(
            "RULE-SITE-LOOKUP",
            "jsonlogic",
            "answers[]",
            "Site reference must be in valid_site_ids",
            JsonNode.Parse("""{ "in": [ { "var": "site_ref" }, { "var": "valid_site_ids" } ] }""")!)
        .AddCustomRule(
            "RULE-NUMERIC-HELPER",
            "jsonlogic",
            "answers[]",
            "Answer value must be numeric",
            JsonNode.Parse("""{ "==": [ { "var": "value_is_numeric" }, true ] }""")!)
        .BuildList();

    [Fact]
    public async Task QualityRuleCoordinator_Without_Enrichers_Should_Not_Inject_Estate_Or_Survey_Helpers()
    {
        // Arrange
        var rules = BuildStandardRules();
        var sut = new QualityRuleCoordinator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            contextEnrichers: []);

        // Act
        var result = (await sut.ValidateAsync(EnrichmentTestPayload, rules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task QualityRuleCoordinator_With_Default_Enrichers_Should_Pass_Valid_References_And_Helpers()
    {
        // Arrange
        var rules = BuildStandardRules();
        var sut = new QualityRuleCoordinator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            [new EstateAmbientReferenceEnricher(), new SurveyAnswerContextEnricher()]);

        // Act
        var result = (await sut.ValidateAsync(EnrichmentTestPayload, rules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task QualityRuleCoordinator_Should_Prevent_Spoofed_Payload_Properties_From_Overriding_Ambient_Enrichment()
    {
        // Arrange
        var rules = BuildStandardRules();
        const string spoofedPayload = """
        {
          "valid_site_ids": ["SPOOFED-SITE"],
          "sites": [{ "id": "SITE-1", "name": "North Campus" }],
          "answers": [{ "site_ref": "SPOOFED-SITE", "valid_site_ids": ["SPOOFED-SITE"], "value": "42" }]
        }
        """;
        var sut = new QualityRuleCoordinator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            [new EstateAmbientReferenceEnricher(), new SurveyAnswerContextEnricher()]);

        // Act
        var result = (await sut.ValidateAsync(spoofedPayload, rules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.RuleId == "RULE-SITE-LOOKUP");
    }

    [Fact]
    public async Task QualityRuleCoordinator_Should_Pass_Custom_Enricher_Context_To_Engines()
    {
        // Arrange
        var customRules = new QualityRulesBuilder()
            .AddCelRule(
                "CEL-CUSTOM-ENRICHER",
                "answers[]",
                "Custom tenant tier check",
                "this.custom_tenant_tier == 'academy' && this.answer_flagged == false")
            .BuildList();

        var sut = new QualityRuleCoordinator(
            [new CelQualityEngine()],
            [new TestCustomContextEnricher()]);

        // Act
        var result = (await sut.ValidateAsync(EnrichmentTestPayload, customRules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    private sealed class TestCustomContextEnricher : IRuleContextEnricher
    {
        public void PopulateAmbientContext(JsonNode rootNode, IDictionary<string, JsonNode?> ambientContext)
        {
            ambientContext["custom_tenant_tier"] = "academy";
        }

        public void EnrichTargetContext(JsonObject enrichedTarget, JsonNode? childFocusNode, JsonNode rootNode)
        {
            enrichedTarget["answer_flagged"] = false;
        }
    }
}
