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
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class ValidationPolicySeverityGatingTests
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

    private const string WarningTriggeringPayload = """{"urn":"EST-100","count":25,"status":"ACTIVE"}""";

    private static BusinessRule BuildWarningRule() => new()
    {
        Id = "WARN-THRESHOLD",
        Engine = "cel",
        Path = "/",
        Severity = RuleSeverity.Warning,
        Description = "Count should ideally be <= 10",
        Implementation = JsonValue.Create("count <= 10")
    };

    private static ResolvedSchema BuildSchema(string name, RuleSeverity failOnMinimumSeverity) =>
        new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m
                .WithName(name)
                .WithSchema(SimpleSchema)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "EST", JsonPointer = "/urn", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy { FailOnMinimumSeverity = failOnMinimumSeverity }))
            .WithSchemaJson(SimpleSchema)
            .WithRules([BuildWarningRule()])
            .Build();

    [Fact]
    public async Task DefaultPolicy_When_FailOnMinimumSeverity_Is_Error_Should_Allow_Warning_Without_Blocking()
    {
        // Arrange
        var schema = BuildSchema("warn_lenient", RuleSeverity.Error);
        var validationService = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Act
        var report = (await validationService.ValidatePayloadWithTraceAsync(WarningTriggeringPayload, schema)).ShouldSucceed();

        // Assert
        report.IsSuccessful(RuleSeverity.Error).Should().BeTrue();
        report.Errors.Should().BeEmpty();
        report.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e => e.RuleId == "WARN-THRESHOLD");
    }

    [Fact]
    public async Task StrictPolicy_When_FailOnMinimumSeverity_Is_Warning_Should_Mark_Draft_Unsuccessful_And_Block_Finalise()
    {
        // Arrange
        var strictSchema = BuildSchema("warn_strict", RuleSeverity.Warning);
        var validationService = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        var session = new SubmissionSessionBuilder().WithSchema("warn_strict", 1).Build();
        var infra = Substitute.For<ISubmissionInfrastructure>();
        var registry = Substitute.For<ISchemaRegistry>();

        infra.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        infra.GetDraftItemsAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SubmissionDraftItem>>.Success([]));
        infra.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(WarningTriggeringPayload));
        infra.SaveDraftPayloadAsync(session.SubmissionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("draft.json"));
        registry.GetExplicitSchemaAsync("warn_strict", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(strictSchema));

        var evalService = new SubmissionEvaluationService(infra, registry, validationService);
        var updateUseCase = new UpdateSubmissionUseCase(infra, evalService);
        var finaliseUseCase = new FinaliseSubmissionUseCase(infra, evalService);

        // Act
        var updateResult = (await updateUseCase.ExecuteAsync(new UpdateSubmissionRequest(session.SubmissionId, WarningTriggeringPayload))).ShouldSucceed();
        var finaliseResult = await finaliseUseCase.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId));

        // Assert
        updateResult.IsSuccessful.Should().BeFalse();
        updateResult.PendingErrors.Should().ContainSingle(e => e.RuleId == "WARN-THRESHOLD" && e.Severity == RuleSeverity.Warning);
        updateResult.Issues![RuleSeverity.Warning].Should().ContainSingle();

        finaliseResult.IsFailure.Should().BeTrue();
        finaliseResult.Error.ValidationErrors.Should().ContainSingle(e => e.RuleId == "WARN-THRESHOLD");
    }
}
