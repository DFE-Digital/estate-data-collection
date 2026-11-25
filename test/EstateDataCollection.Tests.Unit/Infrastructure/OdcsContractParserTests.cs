using AwesomeAssertions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class OdcsContractParserTests
{
    private readonly OdcsContractParser _sut = new();

    [Theory]
    [InlineData("contracts/edc-school-site-jsonlogic.odcs.yaml", "edc-survey-sites", "CCDSurveySubmission-Sites")]
    [InlineData("contracts/edc-survey-elements.odcs.yaml", "edc-survey-elements", "CCDSurveySubmission-Elements")]
    [InlineData("contracts/edc-survey-evidenceandanswers.odcs.yaml", "edc-survey-answers-evidence", "CCDSurveySubmission-AnswersEvidence")]
    public void Should_Parse_All_Three_Cde_Contracts_And_Extract_Quality_Rules(
        string contractRelativePath,
        string expectedContractId,
        string expectedName)
    {
        var yaml = CdeContractFixtureLoader.LoadFile(contractRelativePath);

        _sut.IsOdcsYaml(yaml, Path.GetFileName(contractRelativePath)).Should().BeTrue();
        var parsed = _sut.Parse(yaml).ShouldSucceed();

        parsed.ContractId.Should().Be(expectedContractId);
        parsed.Name.Should().Be(expectedName);
        parsed.Version.Should().Be("1.0.0");
        parsed.GeneratedSchemaJson.Should().NotBeNullOrWhiteSpace();
        parsed.ExtractedRules.Should().ContainSingle();

        var rule = parsed.ExtractedRules[0];
        rule.Id.Should().Be("CDC-DQ1-JSONLogic");
        rule.Engine.Should().Be("jsonlogic");
        rule.Path.Should().Be("survey");
        rule.EffectiveMessage.Should().Contain("Survey updated timestamp must be greater than or equal to survey creation timestamp");
        rule.EffectiveAssertion.Should().NotBeNull();
    }

    [Fact]
    public void IsOdcsYaml_Should_Distinguish_Json_And_Yaml_By_FileType_And_Content()
    {
        var schemaJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json");
        var odcsYaml = CdeContractFixtureLoader.LoadFile("contracts/edc-school-site-jsonlogic.odcs.yaml");

        _sut.IsOdcsYaml(schemaJson, "edc-school-site.schema.json", "application/json").Should().BeFalse();
        _sut.IsOdcsYaml(odcsYaml, "edc-school-site-jsonlogic.odcs.yaml", "application/x-yaml").Should().BeTrue();
        _sut.IsOdcsYaml(odcsYaml, null, null).Should().BeTrue();
    }

    [Fact]
    public void Parse_When_Yaml_Exceeds_Max_Recursion_Depth_Should_Fail_Validation()
    {
        var deeplyNestedYaml = "apiVersion: v3.0.2\nkind: DataContract\nschema:\n  - name: test\n    nested:\n";
        for (var i = 0; i < 80; i++)
        {
            deeplyNestedYaml += new string(' ', (i + 3) * 2) + $"level_{i}:\n";
        }
        deeplyNestedYaml += new string(' ', 83 * 2) + "leaf: value\n";

        var result = _sut.Parse(deeplyNestedYaml);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_Extract_ValidationPolicy_From_CustomProperties_And_Rule_Runbook_Fields()
    {
        const string yaml = """
        apiVersion: v3.0.2
        kind: DataContract
        id: policy-contract
        name: PolicyContract
        version: 1.0.0
        customProperties:
          - property: failOnMinimumSeverity
            value: WARNING
          - property: workflowPipelineKey
            value: StrictPipeline
          - property: crossSubmissionDraftPolicy
            value: BlockFinaliseOnDraftReferences
          - property: numericToleranceOverride
            value: "0.005"
        schema:
          - name: root
            logicalType: object
            properties:
              - name: count
                logicalType: integer
                required: true
            quality:
              - id: R-CRIT
                severity: CRITICAL
                stage: 10
                order: 1
                stopOnFailure: true
                action: reject_submission
                engine: cel
                description: Count must be non-negative
                implementation: "count >= 0"
              - id: R-DEP
                severity: INFO
                stage: 20
                order: 2
                dependsOn: ["R-CRIT"]
                action: allow_submission
                engine: cel
                description: Advisory info notice
        """;

        var parsed = _sut.Parse(yaml).ShouldSucceed();

        parsed.ValidationPolicy.Should().NotBeNull();
        parsed.ValidationPolicy!.FailOnMinimumSeverity.Should().Be(RuleSeverity.Warning);
        parsed.ValidationPolicy.WorkflowPipelineKey.Should().Be("StrictPipeline");
        parsed.ValidationPolicy.CrossSubmissionDraftPolicy.Should().Be(CrossSubmissionDraftPolicy.BlockFinaliseOnDraftReferences);
        parsed.ValidationPolicy.NumericToleranceOverride.Should().Be(0.005m);

        parsed.ExtractedRules.Should().HaveCount(2);
        var crit = parsed.ExtractedRules[0];
        crit.Id.Should().Be("R-CRIT");
        crit.Severity.Should().Be(RuleSeverity.Critical);
        crit.Stage.Should().Be(10);
        crit.Order.Should().Be(1);
        crit.EffectiveStopOnFailure.Should().BeTrue();
        crit.Action.Should().Be("reject_submission");

        var dep = parsed.ExtractedRules[1];
        dep.Id.Should().Be("R-DEP");
        dep.Severity.Should().Be(RuleSeverity.Info);
        dep.Stage.Should().Be(20);
        dep.Order.Should().Be(2);
        dep.DependsOn.Should().Equal("R-CRIT");
        dep.EffectiveAssertion.Should().BeNull();
    }
}
