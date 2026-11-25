using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class QualityRuleCoordinatorRunbookTests
{
    private const string SimpleSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["urn", "count", "status"],
      "properties": {
        "urn": { "type": "string" },
        "count": { "type": "integer" },
        "status": { "type": "string" },
        "note": { "type": "string" }
      }
    }
    """;

    [Fact]
    public void RuleSeverity_Should_Parse_CaseInsensitively_And_Serialize_Issues_Dictionary_With_Lowercase_Keys()
    {
        const string rulesJson = """
        [
          { "id": "R1", "path": "/", "severity": "CRITICAL", "description": "Critical rule", "engine": "cel", "implementation": "count > 0" },
          { "id": "R2", "path": "/", "severity": "warn", "description": "Warn rule", "engine": "cel", "implementation": "count < 10" },
          { "id": "R3", "path": "/", "severity": "info", "description": "Advisory note" }
        ]
        """;

        var coordinator = new QualityRuleCoordinator();
        var syntaxErrors = coordinator.ValidateRulesSyntax(rulesJson, out var parsed).ShouldSucceed();

        syntaxErrors.Should().BeEmpty();
        parsed.Should().HaveCount(3);
        parsed![0].Severity.Should().Be(RuleSeverity.Critical);
        parsed[0].EffectiveStopOnFailure.Should().BeTrue();
        parsed[1].Severity.Should().Be(RuleSeverity.Warning);
        parsed[1].EffectiveStopOnFailure.Should().BeFalse();
        parsed[2].Severity.Should().Be(RuleSeverity.Info);
        parsed[2].EffectiveAssertion.Should().BeNull();

        var grouped = ValidationIssuesHelper.GroupBySeverity([
            new ValidationError("/", "crit", "R1", RuleSeverity.Critical, "reject_submission", "cel"),
            new ValidationError("/", "warn", "R2", RuleSeverity.Warning, "highlight_issue", "cel")
        ]);

        var serialized = JsonSerializer.Serialize(grouped);
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

        var eval = (await coordinator.ValidateAsync("""{"urn":"EST-1","count":50,"status":"OK"}""", rules!)).ShouldSucceed();

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

        var eval = (await coordinator.ValidateAsync("""{"urn":"EST-1","count":0,"status":"DRAFT"}""", rules!)).ShouldSucceed();

        // Only STAGE1-PREREQ should emit an issue:
        // STAGE1-DEPENDENT skipped via DependsOn, STAGE2-NORMAL halted after Critical stage 10 failure.
        eval.Errors.Should().ContainSingle(e => e.RuleId == "STAGE1-PREREQ" && e.Severity == RuleSeverity.Critical);
        eval.EngineTraces.Should().Contain(t => t.Descriptor.EngineId == "jsonlogic" && t.Outcome == "Skipped");
    }

    [Fact]
    public async Task QualityRuleCoordinator_When_Critical_Has_StopOnFailure_False_Should_Continue_Subsequent_Stages()
    {
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

        var eval = (await coordinator.ValidateAsync("""{"urn":"EST-1","count":0,"status":"DRAFT"}""", rules!)).ShouldSucceed();

        eval.Errors.Should().HaveCount(2);
        eval.Errors.Select(e => e.RuleId).Should().Equal("STAGE1-CRIT-NOHALT", "STAGE2-ERROR");
    }

    [Fact]
    public async Task SchemaValidationPolicy_FailOnMinimumSeverity_Should_Gate_IsSuccessful_And_Finalise()
    {
        var warningRule = new BusinessRule
        {
            Id = "WARN-THRESHOLD",
            Engine = "cel",
            Path = "/",
            Severity = RuleSeverity.Warning,
            Description = "Count should ideally be <= 10",
            Implementation = JsonValue.Create("count <= 10")
        };

        var validationService = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Case 1: Default policy (FailOnMinimumSeverity = Error) -> warning does NOT fail IsSuccessful or Finalise
        var lenientSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m
                .WithName("warn_lenient")
                .WithSchema(SimpleSchema)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "EST", JsonPointer = "/urn", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy { FailOnMinimumSeverity = RuleSeverity.Error }))
            .WithSchemaJson(SimpleSchema)
            .WithRules([warningRule])
            .Build();

        const string payload = """{"urn":"EST-100","count":25,"status":"ACTIVE"}""";

        var lenientReport = (await validationService.ValidatePayloadWithTraceAsync(payload, lenientSchema)).ShouldSucceed();
        lenientReport.IsSuccessful(RuleSeverity.Error).Should().BeTrue();
        lenientReport.Errors.Should().BeEmpty();
        lenientReport.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e => e.RuleId == "WARN-THRESHOLD");

        // Case 2: Strict policy (FailOnMinimumSeverity = Warning) -> warning FAILS IsSuccessful & Finalise
        var strictSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m
                .WithName("warn_strict")
                .WithSchema(SimpleSchema)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "EST", JsonPointer = "/urn", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy { FailOnMinimumSeverity = RuleSeverity.Warning }))
            .WithSchemaJson(SimpleSchema)
            .WithRules([warningRule])
            .Build();

        var session = new SubmissionSessionBuilder().WithSchema("warn_strict", 1).Build();
        var infra = Substitute.For<ISubmissionInfrastructure>();
        var registry = Substitute.For<ISchemaRegistry>();

        infra.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        infra.GetDraftItemsAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SubmissionDraftItem>>.Success([]));
        infra.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(payload));
        infra.SaveDraftPayloadAsync(session.SubmissionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("draft.json"));
        registry.GetExplicitSchemaAsync("warn_strict", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(strictSchema));

        var evalService = new SubmissionEvaluationService(infra, registry, validationService);
        var updateUseCase = new UpdateSubmissionUseCase(infra, evalService);
        var updateResult = (await updateUseCase.ExecuteAsync(new UpdateSubmissionRequest(session.SubmissionId, payload))).ShouldSucceed();

        updateResult.IsSuccessful.Should().BeFalse();
        updateResult.PendingErrors.Should().ContainSingle(e => e.RuleId == "WARN-THRESHOLD" && e.Severity == RuleSeverity.Warning);
        updateResult.Issues![RuleSeverity.Warning].Should().ContainSingle();

        var finaliseUseCase = new FinaliseSubmissionUseCase(infra, evalService);
        var finaliseResult = await finaliseUseCase.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId));
        finaliseResult.IsFailure.Should().BeTrue();
        finaliseResult.Error.ValidationErrors.Should().ContainSingle(e => e.RuleId == "WARN-THRESHOLD");
    }
}
