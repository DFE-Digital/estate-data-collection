using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Workflow;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class CrossSubmissionEnrichmentTests
{
    private const string SchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["establishment", "siteId"],
      "properties": {
        "establishment": {
          "type": "object",
          "required": ["laestab"],
          "properties": { "laestab": { "type": "string" } }
        },
        "siteId": { "type": "string" }
      }
    }
    """;

    private const string ValidPayload = """{"establishment":{"laestab":"201-3614"},"siteId":"SITE-1"}""";

    private static (PayloadValidationService Service, Guid CurrentSubId, Guid DraftSiblingId) CreateHarness()
    {
        var currentSubId = Guid.NewGuid();
        var draftSiblingId = Guid.NewGuid();
        var finalisedSiblingId = Guid.NewGuid();

        var indexRepo = Substitute.For<IIdentifierIndexRepository>();
        var draftSibling = new IdentifierIndexEntry(
            draftSiblingId, "site-baseline", 1, "Draft", DateTimeOffset.UtcNow.AddMinutes(-2), $"{draftSiblingId}/draft.json");
        var finalisedSibling = new IdentifierIndexEntry(
            finalisedSiblingId, "block-baseline", 1, "Finalised", DateTimeOffset.UtcNow.AddMinutes(-10),
            ProcessedBundlePrefix: $"laestab=201-3614/schema=block-baseline/v=1/{finalisedSiblingId}/");
        var selfEntry = new IdentifierIndexEntry(
            currentSubId, "elemental-survey", 1, "Draft", DateTimeOffset.UtcNow);

        indexRepo.GetEntriesAsync("LAEstab", "201-3614", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<IdentifierIndexEntry>>.Success([selfEntry, draftSibling, finalisedSibling]));
        indexRepo.ReadSiblingPayloadAsync(draftSibling, Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("""{"sites":[{"id":"SITE-1","blocks":[{"id":"BLK-A"}]}]}"""));
        indexRepo.ReadSiblingPayloadAsync(finalisedSibling, Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("""{"blocks":[{"id":"BLK-B"}]}"""));

        var runner = ValidationWorkflowRunner.CreateDefault(
            new JsonSchemaNetValidator(),
            new QualityRuleCoordinator(),
            identifierIndexRepository: indexRepo);
        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator(), workflowRunner: runner);

        return (service, currentSubId, draftSiblingId);
    }

    private static ResolvedSchema BuildSchemaWithPolicy(SchemaValidationPolicy policy)
    {
        IReadOnlyList<BusinessRule> rules =
        [
            new()
            {
                Id = "CROSS-SITE-EXISTS",
                Engine = "cel",
                Path = "",
                Severity = RuleSeverity.Error,
                Description = "Site must exist in cross-submission baseline",
                Implementation = JsonValue.Create("this.siteId in ambient.cross_submission.known_site_ids")
            }
        ];

        return new ResolvedSchemaBuilder()
            .WithSchemaJson(SchemaJson)
            .WithRules(rules)
            .ConfigureMetadata(m => m
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab", Required = true })
                .WithValidationPolicy(policy))
            .Build();
    }

    [Fact]
    public async Task DraftOperation_Should_Allow_DraftSiblings_And_Exclude_Self()
    {
        // Arrange
        var (service, currentSubId, _) = CreateHarness();
        var schema = BuildSchemaWithPolicy(new SchemaValidationPolicy
        {
            CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.BlockFinaliseOnDraftReferences
        });
        using var scope = SubmissionExecutionScope.Start("DraftCheck", currentSubId);

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            ValidPayload,
            schema,
            ValidationOperationKind.SubmissionDraft)).ShouldSucceed();

        // Assert
        report.Errors.Should().BeEmpty();
        var crossTrace = report.EngineTraces!.Single(t => t.Descriptor.EngineId == WorkflowStepIds.CrossSubmissionEnrichment);
        crossTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        crossTrace.ItemsEvaluated.Should().Be(2);
    }

    [Fact]
    public async Task FinaliseOperation_Under_BlockPolicy_Should_Block_DraftReferences()
    {
        // Arrange
        var (service, currentSubId, draftSiblingId) = CreateHarness();
        var schema = BuildSchemaWithPolicy(new SchemaValidationPolicy
        {
            CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.BlockFinaliseOnDraftReferences
        });
        using var scope = SubmissionExecutionScope.Start("FinaliseCheck", currentSubId);

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            ValidPayload,
            schema,
            ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();

        // Assert
        report.Errors.Should().ContainSingle(e =>
            e.RuleId == "CrossSubmission.DraftReferenceBlocked" &&
            e.Severity == RuleSeverity.Error &&
            e.Message.Contains(draftSiblingId.ToString()));
    }

    [Fact]
    public async Task FinaliseOperation_Under_WarnPolicy_And_ErrorGate_Should_Emit_NonBlocking_Warning()
    {
        // Arrange
        var (service, currentSubId, _) = CreateHarness();
        var schema = BuildSchemaWithPolicy(new SchemaValidationPolicy
        {
            CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.WarnOnDraftReferences,
            FailOnMinimumSeverity = RuleSeverity.Error
        });
        using var scope = SubmissionExecutionScope.Start("WarnCheck", currentSubId);

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            ValidPayload,
            schema,
            ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();

        // Assert
        report.Errors.Should().BeEmpty();
        report.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e =>
            e.RuleId == "CrossSubmission.DraftReferenceWarning");
    }

    [Fact]
    public async Task FinaliseOperation_Under_WarnPolicy_And_WarningGate_Should_Block_Finalisation()
    {
        // Arrange
        var (service, currentSubId, _) = CreateHarness();
        var schema = BuildSchemaWithPolicy(new SchemaValidationPolicy
        {
            CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.WarnOnDraftReferences,
            FailOnMinimumSeverity = RuleSeverity.Warning
        });
        using var scope = SubmissionExecutionScope.Start("StrictWarnCheck", currentSubId);

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            ValidPayload,
            schema,
            ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();

        // Assert
        report.Errors.Should().ContainSingle(e => e.RuleId == "CrossSubmission.DraftReferenceWarning");
    }

    [Fact]
    public async Task FinaliseOperation_Under_AllowPolicy_Should_Emit_No_DraftReference_Issues()
    {
        // Arrange
        var (service, currentSubId, _) = CreateHarness();
        var schema = BuildSchemaWithPolicy(new SchemaValidationPolicy
        {
            CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.AllowDraftReferences
        });
        using var scope = SubmissionExecutionScope.Start("AllowCheck", currentSubId);

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            ValidPayload,
            schema,
            ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();

        // Assert
        report.Errors.Should().BeEmpty();
        report.EffectiveIssues.Values.SelectMany(x => x).Should().BeEmpty();
    }
}
