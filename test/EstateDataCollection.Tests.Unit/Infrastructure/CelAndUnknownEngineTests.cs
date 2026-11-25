using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Infrastructure.Validation.Engines;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Microsoft.Extensions.Options;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class CelAndUnknownEngineTests
{
    [Fact]
    public async Task CelQualityEngine_When_Valid_Payload_Should_Pass_Timestamps_Arrays_And_Macros()
    {
        // Arrange
        var celRulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-DQ1-TIMESTAMP", "survey", "CEL: Survey updated_at must be >= created_at using RFC3339 timestamp()",
                "timestamp(survey.updated_at) >= timestamp(survey.created_at)")
            .AddCelRule("CEL-DQ2-GRADE-D-REPAIRS", "elements[].occurrences[]", "CEL: Grade D occurrences must have at least one repair",
                "this.condition_grade != 'D' || size(this.repairs) > 0")
            .AddCelRule("CEL-DQ3-REPAIR-COST", "elements[].occurrences[].repairs[]", "CEL: Estimated cost must equal repair_quantity * repair_rate",
                "double(this.estimated_cost) == double(this.repair_quantity) * double(this.repair_rate)")
            .BuildJson();

        var coordinator = new QualityRuleCoordinator();
        var syntaxErrors = coordinator.ValidateRulesSyntax(celRulesJson, out var parsedRules).ShouldSucceed();

        // Act
        var outcome = (await coordinator.ValidateAsync(CdePayloadFixtures.ValidSurveyElementsPayload, parsedRules!)).ShouldSucceed();

        // Assert
        syntaxErrors.Should().BeEmpty();
        parsedRules.Should().HaveCount(3);
        outcome.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CelQualityEngine_When_Rules_Violated_Should_Report_Targeted_Paths()
    {
        // Arrange
        var celRulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-DQ1-TIMESTAMP", "survey", "CEL: Survey updated_at must be >= created_at using RFC3339 timestamp()",
                "timestamp(survey.updated_at) >= timestamp(survey.created_at)")
            .AddCelRule("CEL-DQ2-GRADE-D-REPAIRS", "elements[].occurrences[]", "CEL: Grade D occurrences must have at least one repair",
                "this.condition_grade != 'D' || size(this.repairs) > 0")
            .BuildJson();

        var coordinator = new QualityRuleCoordinator();
        coordinator.ValidateRulesSyntax(celRulesJson, out var parsedRules).ShouldSucceed();

        var mutated = JsonNode.Parse(CdePayloadFixtures.ValidSurveyElementsPayload)!;
        mutated["survey"]!["updated_at"] = "2026-03-20T00:00:00Z";
        mutated["elements"]![0]!["occurrences"]![0]!["repairs"] = new JsonArray();

        // Act
        var outcome = (await coordinator.ValidateAsync(mutated.ToJsonString(), parsedRules!)).ShouldSucceed();

        // Assert
        outcome.IsValid.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.PropertyPath == "/survey" && e.Message.Contains("CEL: Survey updated_at must be >= created_at"));
        outcome.Errors.Should().Contain(e => e.PropertyPath == "/elements/0/occurrences/0" && e.Message.Contains("CEL: Grade D occurrences must have at least one repair"));
    }

    [Fact]
    public void CelQualityEngine_When_Expression_Has_Invalid_Syntax_Should_Report_Syntax_Error()
    {
        // Arrange
        var invalidCelRulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-BROKEN", "survey", "Broken CEL syntax", "survey.updated_at >= == (")
            .BuildJson();

        var coordinator = new QualityRuleCoordinator();

        // Act
        var syntaxErrors = coordinator.ValidateRulesSyntax(invalidCelRulesJson, out var parsedRules).ShouldSucceed();

        // Assert
        syntaxErrors.Should().ContainSingle(e => e.Identifier == "CEL-BROKEN" && e.Message.Contains("Invalid CEL expression syntax"));
        parsedRules.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_Engine_Under_Fail_Behavior_Should_Reject_At_Syntax_And_Runtime()
    {
        // Arrange
        var sqlRuleJson = new QualityRulesBuilder()
            .AddCustomRule("DQ-SQL-1", "sql", "survey", "Downstream SQL check",
                "SELECT COUNT(*) FROM survey WHERE updated_at < created_at")
            .BuildJson();

        var strictCoordinator = new QualityRuleCoordinator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            Options.Create(new ValidationOptions { UnknownEngineBehavior = UnknownEngineBehavior.Fail }));

        var lenientCoordinator = new QualityRuleCoordinator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            Options.Create(new ValidationOptions { UnknownEngineBehavior = UnknownEngineBehavior.Skip }));

        lenientCoordinator.ValidateRulesSyntax(sqlRuleJson, out var parsedRules).ShouldSucceed();

        // Act
        var strictSyntax = strictCoordinator.ValidateRulesSyntax(sqlRuleJson, out _).ShouldSucceed();
        var runtimeStrict = (await strictCoordinator.ValidateAsync(CdePayloadFixtures.ValidSchoolSitePayload, parsedRules!)).ShouldSucceed();

        // Assert
        strictSyntax.Should().ContainSingle(e =>
            e.Identifier == "DQ-SQL-1" &&
            e.Message.Contains("Quality rule engine 'sql' is not enabled or supported"));
        runtimeStrict.IsValid.Should().BeFalse();
        runtimeStrict.Errors.Should().ContainSingle(e => e.Message.Contains("engine 'sql' is not enabled or supported"));
    }

    [Fact]
    public async Task Unknown_Engine_Under_Skip_Behavior_Should_Skip_Cleanly_At_Syntax_And_Runtime()
    {
        // Arrange
        var sqlRuleJson = new QualityRulesBuilder()
            .AddCustomRule("DQ-SQL-1", "sql", "survey", "Downstream SQL check",
                "SELECT COUNT(*) FROM survey WHERE updated_at < created_at")
            .BuildJson();

        var lenientCoordinator = new QualityRuleCoordinator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            Options.Create(new ValidationOptions { UnknownEngineBehavior = UnknownEngineBehavior.Skip }));

        // Act
        var syntaxErrors = lenientCoordinator.ValidateRulesSyntax(sqlRuleJson, out var parsedRules).ShouldSucceed();
        var runtimeOutcome = (await lenientCoordinator.ValidateAsync(CdePayloadFixtures.ValidSchoolSitePayload, parsedRules!)).ShouldSucceed();

        // Assert
        syntaxErrors.Should().BeEmpty();
        parsedRules.Should().NotBeNull();
        runtimeOutcome.IsValid.Should().BeTrue();
    }
}
