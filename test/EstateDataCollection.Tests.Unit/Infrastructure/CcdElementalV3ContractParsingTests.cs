using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Builders.Payloads;
using EstateDataCollection.Tests.Common.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class CcdElementalV3ContractParsingTests
{
    [Fact]
    public void OdcsContractParser_And_ConsistencyAnalyser_Should_Validate_All_57_CcdElementalV3_Rules()
    {
        // Arrange
        var yaml = CcdElementalV3ContractBuilder.DefaultOdcsYaml;
        var parser = new OdcsContractParser();
        var coordinator = new QualityRuleCoordinator();
        var analyser = new ContractConsistencyAnalyser();
        var expectedIds = Enumerable.Range(1, 57)
            .Select(i => $"CCD-ELEM-V3-{i:D3}")
            .ToArray();

        // Act
        var parsed = parser.Parse(yaml).ShouldSucceed();
        var syntaxErrors = coordinator.ValidateRulesSyntax(parsed.GeneratedRulesJson!, out _).ShouldSucceed();
        var consistency = analyser.Analyse(parsed.GeneratedSchemaJson, parsed.ExtractedRules);

        // Assert
        parsed.Name.Should().Be("ccd-elemental-v3");
        parsed.ValidationPolicy.Should().NotBeNull();
        parsed.ValidationPolicy!.FailOnMinimumSeverity.Should().Be(RuleSeverity.Error);
        parsed.ValidationPolicy.CrossSubmissionDraftPolicy.Should().Be(CrossSubmissionDraftPolicy.WarnOnDraftReferences);
        parsed.ValidationPolicy.WorkflowPipelineKey.Should().BeNull();
        parsed.ExtractedRules.Should().HaveCount(57);
        parsed.ExtractedRules.Select(r => r.Id).Should().BeEquivalentTo(expectedIds);

        syntaxErrors.Should().BeEmpty();
        consistency.Findings.Should().BeEmpty();
        consistency.Errors.Should().BeEmpty();
        consistency.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task SchemaVerificationService_Should_Verify_CcdElementalV3_Contract_With_Valid_And_Invalid_Fixtures()
    {
        // Arrange
        var yaml = CcdElementalV3ContractBuilder.DefaultOdcsYaml;
        var validPayload = CcdElementalV3PayloadBuilder.DefaultValidPayloadJson;
        var ambientFixtures = CcdElementalV3PayloadBuilder.BuildAmbientDictionary();

        var invalidPayload1 = new CcdElementalV3PayloadBuilder()
            .Mutate(doc => doc["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["repairItem"] = "Roof Tile")
            .BuildJson();
        var invalidPayload2 = new CcdElementalV3PayloadBuilder()
            .Mutate(doc => doc["elements"]![0]!["occurrences"]![0]!["photoReference"] = null)
            .BuildJson();

        string[] invalidSpecs =
        [
            JsonNode.Parse($$"""{"description":"Wrong repairItem for Boiler","payload":{{invalidPayload1}},"expectedErrors":["CCD-ELEM-V3-019"]}""")!.ToJsonString(),
            JsonNode.Parse($$"""{"description":"Grade D missing photoReference","payload":{{invalidPayload2}},"expectedErrors":["CCD-ELEM-V3-002"]}""")!.ToJsonString()
        ];

        var verifier = new SchemaVerificationService(
            new QualityRuleCoordinator(),
            new OdcsContractParser(),
            Options.Create(new ValidationOptions()),
            consistencyAnalyser: new ContractConsistencyAnalyser());

        // Act
        var result = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: yaml,
            rawRulesJson: null,
            sampleValidPayloads: [validPayload],
            sampleInvalidPayloads: invalidSpecs,
            identifierConfig: null,
            cancellationToken: CancellationToken.None,
            validationPolicy: null,
            ambientFixtures: ambientFixtures)).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.HasWarnings.Should().BeFalse();
        result.AnalyserFindings.Should().BeEmpty();
        result.Issues[RuleSeverity.Info].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-038");
        result.InvalidFixtureOutcomes.Should().HaveCount(2);
        result.InvalidFixtureOutcomes!.Should().OnlyContain(o => o.FailedAsExpected);
    }

    [Fact]
    public async Task Runbook_Critical_Stage10_Failure_Should_Halt_Subsequent_Stages()
    {
        // Arrange
        var parsed = new OdcsContractParser().Parse(CcdElementalV3ContractBuilder.DefaultOdcsYaml).ShouldSucceed();
        var ambientFixtures = CcdElementalV3PayloadBuilder.BuildAmbientDictionary();
        var coordinator = new QualityRuleCoordinator();
        var payload = new CcdElementalV3PayloadBuilder()
            .Mutate(doc =>
            {
                doc["survey"]!["updatedAt"] = "2026-04-01T00:00:00Z";
                doc["elements"]![0]!["productType"] = "InvalidProduct";
            })
            .BuildJson();

        // Act
        var result = (await coordinator.ValidateAsync(payload, parsed.ExtractedRules, ambientFixtures)).ShouldSucceed();

        // Assert
        result.EffectiveIssues[RuleSeverity.Critical].Should().ContainSingle(e => e.RuleId == "CCD-ELEM-V3-003");
        result.EffectiveIssues[RuleSeverity.Error].Should().BeEmpty();
    }

    [Fact]
    public async Task Runbook_DependsOn_Failure_Should_Skip_Downstream_Dependent_Rules()
    {
        // Arrange
        var parsed = new OdcsContractParser().Parse(CcdElementalV3ContractBuilder.DefaultOdcsYaml).ShouldSucceed();
        var ambientFixtures = CcdElementalV3PayloadBuilder.BuildAmbientDictionary();
        var coordinator = new QualityRuleCoordinator();
        var payload = new CcdElementalV3PayloadBuilder()
            .Mutate(doc =>
            {
                doc["elements"]![0]!["productType"] = "InvalidProduct";
                doc["elements"]![0]!["occurrences"]![0]!["basisOfAssessment"] = null;
            })
            .BuildJson();

        // Act
        var result = (await coordinator.ValidateAsync(payload, parsed.ExtractedRules, ambientFixtures)).ShouldSucceed();

        // Assert
        result.EffectiveIssues[RuleSeverity.Error].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-001");
        result.EffectiveIssues[RuleSeverity.Error].Should().NotContain(e => e.RuleId == "CCD-ELEM-V3-016");
        result.EffectiveIssues[RuleSeverity.Error].Should().NotContain(e => e.RuleId == "CCD-ELEM-V3-019");
    }
}
