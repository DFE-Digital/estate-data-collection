using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class RunbookSeverityAndExecutionTests
{
    [Fact]
    public void RuleSeverity_Should_Parse_CaseInsensitively_And_Serialize_Issues_Dictionary_With_Lowercase_Keys()
    {
        // Arrange
        const string rulesJson = """
        [
          { "id": "R1", "path": "/", "severity": "CRITICAL", "description": "Critical rule", "engine": "cel", "implementation": "count > 0" },
          { "id": "R2", "path": "/", "severity": "warn", "description": "Warn rule", "engine": "cel", "implementation": "count < 10" },
          { "id": "R3", "path": "/", "severity": "info", "description": "Advisory note" }
        ]
        """;

        var coordinator = new QualityRuleCoordinator();
        var grouped = ValidationIssuesHelper.GroupBySeverity([
            new ValidationError("/", "crit", "R1", RuleSeverity.Critical, "reject_submission", "cel"),
            new ValidationError("/", "warn", "R2", RuleSeverity.Warning, "highlight_issue", "cel")
        ]);

        // Act
        var syntaxErrors = coordinator.ValidateRulesSyntax(rulesJson, out var parsed).ShouldSucceed();
        var serialized = JsonSerializer.Serialize(grouped);

        // Assert
        syntaxErrors.Should().BeEmpty();
        parsed.Should().HaveCount(3);
        parsed![0].Severity.Should().Be(RuleSeverity.Critical);
        parsed[0].EffectiveStopOnFailure.Should().BeTrue();
        parsed[1].Severity.Should().Be(RuleSeverity.Warning);
        parsed[1].EffectiveStopOnFailure.Should().BeFalse();
        parsed[2].Severity.Should().Be(RuleSeverity.Info);
        parsed[2].EffectiveAssertion.Should().BeNull();

        using var doc = JsonDocument.Parse(serialized);
        doc.RootElement.TryGetProperty("critical", out var critProp).Should().BeTrue();
        critProp.GetArrayLength().Should().Be(1);
        doc.RootElement.TryGetProperty("error", out var errProp).Should().BeTrue();
        errProp.GetArrayLength().Should().Be(0);
        doc.RootElement.TryGetProperty("warning", out var warnProp).Should().BeTrue();
        warnProp.GetArrayLength().Should().Be(1);
        doc.RootElement.TryGetProperty("info", out var infoProp).Should().BeTrue();
        infoProp.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task QualityRuleCoordinator_Should_Evaluate_All_Severities_And_Emit_Advisory_Info_Rules()
    {
        // Arrange
        const string rulesJson = """
        [
          {
            "id": "RULE-WARN-JL",
            "path": "/",
            "severity": "WARNING",
            "description": "High count warning",
            "engine": "jsonlogic",
            "implementation": { "<=": [ { "var": "count" }, 10 ] }
          },
          {
            "id": "RULE-INFO-ADV",
            "path": "/",
            "severity": "INFO",
            "description": "Submission includes advisory check CCD-ELEM-V3-038",
            "engine": "cel"
          }
        ]
        """;

        var coordinator = new QualityRuleCoordinator();
        coordinator.ValidateRulesSyntax(rulesJson, out var rules).ShouldSucceed().Should().BeEmpty();

        // Act
        var eval = (await coordinator.ValidateAsync("""{"urn":"EST-1","count":50,"status":"OK"}""", rules!)).ShouldSucceed();

        // Assert
        eval.Errors.Should().HaveCount(2);
        eval.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e =>
            e.RuleId == "RULE-WARN-JL" &&
            e.Severity == RuleSeverity.Warning &&
            e.Action == "highlight_issue" &&
            e.EngineId == "jsonlogic");
        eval.EffectiveIssues[RuleSeverity.Info].Should().ContainSingle(e =>
            e.RuleId == "RULE-INFO-ADV" &&
            e.Severity == RuleSeverity.Info &&
            e.Action == "allow_submission" &&
            e.PropertyPath == "/");
    }

    [Fact]
    public async Task QualityRuleCoordinator_Should_Enforce_Stage_Order_DependsOn_Skipping_And_Critical_Stage_Halt()
    {
        // Arrange
        const string rulesJson = """
        [
          {
            "id": "STAGE2-NORMAL",
            "path": "/",
            "stage": 20,
            "order": 1,
            "severity": "ERROR",
            "description": "Stage 20 should be halted after stage 10 critical failure",
            "engine": "cel",
            "implementation": "count > 100"
          },
          {
            "id": "STAGE1-DEPENDENT",
            "path": "/",
            "stage": 10,
            "order": 2,
            "dependsOn": ["STAGE1-PREREQ"],
            "severity": "ERROR",
            "description": "Should skip when prereq fails",
            "engine": "jsonlogic",
            "implementation": { "==": [ { "var": "status" }, "ACTIVE" ] }
          },
          {
            "id": "STAGE1-PREREQ",
            "path": "/",
            "stage": 10,
            "order": 1,
            "severity": "CRITICAL",
            "description": "Count must be positive (critical)",
            "engine": "cel",
            "implementation": "count > 0"
          }
        ]
        """;

        var coordinator = new QualityRuleCoordinator();
        coordinator.ValidateRulesSyntax(rulesJson, out var rules).ShouldSucceed().Should().BeEmpty();

        // Act
        var eval = (await coordinator.ValidateAsync("""{"urn":"EST-1","count":0,"status":"DRAFT"}""", rules!)).ShouldSucceed();

        // Assert
        eval.Errors.Should().ContainSingle(e => e.RuleId == "STAGE1-PREREQ" && e.Severity == RuleSeverity.Critical);
        eval.EngineTraces.Should().Contain(t => t.Descriptor.EngineId == "jsonlogic" && t.Outcome == "Skipped");
    }

    [Fact]
    public async Task QualityRuleCoordinator_When_Critical_Has_StopOnFailure_False_Should_Continue_Subsequent_Stages()
    {
        // Arrange
        const string rulesJson = """
        [
          {
            "id": "STAGE1-CRIT-NOHALT",
            "path": "/",
            "stage": 10,
            "order": 1,
            "severity": "CRITICAL",
            "stopOnFailure": false,
            "description": "Critical without halt",
            "engine": "cel",
            "implementation": "count > 0"
          },
          {
            "id": "STAGE2-ERROR",
            "path": "/",
            "stage": 20,
            "order": 1,
            "severity": "ERROR",
            "description": "Stage 20 still runs",
            "engine": "cel",
            "implementation": "status == 'ACTIVE'"
          }
        ]
        """;

        var coordinator = new QualityRuleCoordinator();
        coordinator.ValidateRulesSyntax(rulesJson, out var rules).ShouldSucceed().Should().BeEmpty();

        // Act
        var eval = (await coordinator.ValidateAsync("""{"urn":"EST-1","count":0,"status":"DRAFT"}""", rules!)).ShouldSucceed();

        // Assert
        eval.Errors.Should().HaveCount(2);
        eval.Errors.Select(e => e.RuleId).Should().Equal("STAGE1-CRIT-NOHALT", "STAGE2-ERROR");
    }
}
