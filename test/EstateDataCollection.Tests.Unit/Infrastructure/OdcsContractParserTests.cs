using AwesomeAssertions;
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
}
