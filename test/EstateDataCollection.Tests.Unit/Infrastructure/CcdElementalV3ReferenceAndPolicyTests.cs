using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Workflow;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Builders.Payloads;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class CcdElementalV3ReferenceAndPolicyTests
{
    private static PayloadValidationService CreateValidationService()
    {
        var refProvider = new CcdElementalV3ReferenceDataBuilder().BuildProvider();
        var indexRepo = Substitute.For<IIdentifierIndexRepository>();
        var siblingId = Guid.NewGuid();
        var siblingEntry = new IdentifierIndexEntry(
            siblingId,
            "edc-survey-sites",
            1,
            "Finalised",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            ProcessedBundlePrefix: $"urn=104521/schema=edc-survey-sites/v=1/{siblingId}/");

        indexRepo.GetEntriesAsync("URN", "104521", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<IdentifierIndexEntry>>.Success([siblingEntry]));
        indexRepo.ReadSiblingPayloadAsync(siblingEntry, Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("""{"sites":[{"id":"SITE-MAIN","blocks":[{"id":"BLK-A"},{"id":"BLK-B"}]}]}"""));

        var runner = WorkflowRunner.CreateDefault(
            new JsonSchemaNetValidator(),
            new QualityRuleCoordinator(),
            identifierIndexRepository: indexRepo,
            referenceDataProvider: refProvider);

        return new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator(), workflowRunner: runner);
    }

    [Fact]
    public async Task FullWorkflowPipeline_Should_Evaluate_Valid_Payload_And_Emit_All_Stages()
    {
        // Arrange
        var parsed = new OdcsContractParser().Parse(CcdElementalV3ContractBuilder.DefaultOdcsYaml).ShouldSucceed();
        var validationService = CreateValidationService();
        var resolvedSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(parsed.GeneratedSchemaJson)
            .WithRules(parsed.ExtractedRules)
            .ConfigureMetadata(m => m
                .WithName("ccd-elemental-v3")
                .WithVersion(3)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true })
                .WithValidationPolicy(parsed.ValidationPolicy!))
            .Build();

        // Act
        var report = (await validationService.ValidatePayloadWithTraceAsync(
            CcdElementalV3PayloadBuilder.DefaultValidPayloadJson,
            resolvedSchema,
            WorkflowOperationKind.SubmissionFinalise)).ShouldSucceed();

        // Assert
        report.IsSuccessful(RuleSeverity.Error).Should().BeTrue();
        report.Errors.Should().BeEmpty();
        report.EffectiveIssues[RuleSeverity.Info].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-038");
        report.ProcessesExecuted.Select(p => p.ProcessName).Should().ContainInOrder(
            "JsonSchemaValidator",
            "IdentifierExtractor",
            "CrossSubmissionEnrichment",
            "ReferenceDataEnrichment",
            "QualityRulesValidator");
    }

    [Fact]
    public async Task FullWorkflowPipeline_Should_Allow_Warning_Under_ErrorGate_And_Block_Under_WarningGate()
    {
        // Arrange
        var parsed = new OdcsContractParser().Parse(CcdElementalV3ContractBuilder.DefaultOdcsYaml).ShouldSucceed();
        var validationService = CreateValidationService();
        var warningPayload = new CcdElementalV3PayloadBuilder()
            .Mutate(doc => doc["elements"]![1]!["occurrences"]![0]!["conditionScore"] = 72.0)
            .BuildJson();

        var errorGateSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(parsed.GeneratedSchemaJson)
            .WithRules(parsed.ExtractedRules)
            .ConfigureMetadata(m => m
                .WithName("ccd-elemental-v3")
                .WithVersion(3)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true })
                .WithValidationPolicy(parsed.ValidationPolicy!))
            .Build();

        var strictWarningSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(parsed.GeneratedSchemaJson)
            .WithRules(parsed.ExtractedRules)
            .ConfigureMetadata(m => m
                .WithName("ccd-elemental-v3")
                .WithVersion(3)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy { FailOnMinimumSeverity = RuleSeverity.Warning }))
            .Build();

        // Act
        var errorGateReport = (await validationService.ValidatePayloadWithTraceAsync(
            warningPayload,
            errorGateSchema,
            WorkflowOperationKind.SubmissionFinalise)).ShouldSucceed();

        var strictReport = (await validationService.ValidatePayloadWithTraceAsync(
            warningPayload,
            strictWarningSchema,
            WorkflowOperationKind.SubmissionFinalise)).ShouldSucceed();

        // Assert
        errorGateReport.IsSuccessful(RuleSeverity.Error).Should().BeTrue();
        errorGateReport.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e => e.RuleId == "CCD-ELEM-V3-028");

        strictReport.IsSuccessful(RuleSeverity.Warning).Should().BeFalse();
        strictReport.Errors.Should().ContainSingle(e => e.RuleId == "CCD-ELEM-V3-028");
    }
}
