using AwesomeAssertions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class OdcsContractParserTests
{
    private readonly OdcsContractParser _sut = new();

    public static TheoryData<string, string, string, string> CdeContractCases() => new()
    {
        { "edc-school-site-jsonlogic.odcs.yaml", EdcSchoolSiteContractBuilder.DefaultOdcsYaml, "edc-survey-sites", "CCDSurveySubmission-Sites" },
        { "edc-survey-elements.odcs.yaml", EdcSurveyElementsContractBuilder.DefaultOdcsYaml, "edc-survey-elements", "CCDSurveySubmission-Elements" },
        { "edc-survey-evidenceandanswers.odcs.yaml", EdcEvidenceAnswersContractBuilder.DefaultOdcsYaml, "edc-survey-answers-evidence", "CCDSurveySubmission-AnswersEvidence" }
    };

    [Theory]
    [MemberData(nameof(CdeContractCases))]
    public void Should_Parse_All_Three_Cde_Contracts_And_Extract_Quality_Rules(
        string fileName,
        string yaml,
        string expectedContractId,
        string expectedName)
    {
        // Arrange
        var isYaml = _sut.IsOdcsYaml(yaml, fileName);

        // Act
        var parsed = _sut.Parse(yaml).ShouldSucceed();

        // Assert
        isYaml.Should().BeTrue();
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
        // Arrange
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var odcsYaml = EdcSchoolSiteContractBuilder.DefaultOdcsYaml;

        // Act
        var jsonDetected = _sut.IsOdcsYaml(schemaJson, "edc-school-site.schema.json", "application/json");
        var yamlWithMimeDetected = _sut.IsOdcsYaml(odcsYaml, "edc-school-site-jsonlogic.odcs.yaml", "application/x-yaml");
        var yamlByContentDetected = _sut.IsOdcsYaml(odcsYaml, null, null);

        // Assert
        jsonDetected.Should().BeFalse();
        yamlWithMimeDetected.Should().BeTrue();
        yamlByContentDetected.Should().BeTrue();
    }

    [Fact]
    public void Parse_When_Yaml_Exceeds_Max_Recursion_Depth_Should_Fail_Validation()
    {
        // Arrange
        var deeplyNestedYaml = "apiVersion: v3.0.2\nkind: DataContract\nschema:\n  - name: test\n    nested:\n";
        for (var i = 0; i < 80; i++)
        {
            deeplyNestedYaml += new string(' ', (i + 3) * 2) + $"level_{i}:\n";
        }
        deeplyNestedYaml += new string(' ', 83 * 2) + "leaf: value\n";

        // Act
        var result = _sut.Parse(deeplyNestedYaml);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_Extract_ValidationPolicy_From_CustomProperties_And_Rule_Runbook_Fields()
    {
        // Arrange
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

        // Act
        var parsed = _sut.Parse(yaml).ShouldSucceed();

        // Assert
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
