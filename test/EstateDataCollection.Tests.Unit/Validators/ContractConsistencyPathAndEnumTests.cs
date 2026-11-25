using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Validators;

public sealed class ContractConsistencyPathAndEnumTests
{
    private readonly ContractConsistencyAnalyzer _sut = new();

    internal const string SampleSchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["urn", "siteStatus", "repairQuantity"],
      "properties": {
        "urn": { "type": "integer", "minimum": 1 },
        "siteStatus": {
          "type": "string",
          "enum": ["Open", "Closed", "Mothballed"]
        },
        "repairQuantity": { "type": "number", "minimum": 0, "maximum": 100 },
        "notes": { "type": "string" },
        "sites": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "siteName": { "type": "string" },
              "productType": { "type": "string" }
            }
          }
        },
        "elements": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "productType": { "type": "string" },
              "occurrences": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "surveyLevel": { "type": "string" },
                    "conditionGrade": { "type": "string" }
                  }
                }
              }
            }
          }
        }
      }
    }
    """;

    [Fact]
    public void Analyze_Should_Flag_Missing_Rule_Path_With_Fuzzy_Suggestion()
    {
        // Arrange
        BusinessRule[] rules =
        [
            new()
            {
                Id = "R-PATH-TYPO",
                Engine = "cel",
                Path = "repairQuantty",
                Severity = RuleSeverity.Error,
                Implementation = "this >= 0"
            }
        ];

        // Act
        var report = _sut.Analyze(SampleSchemaJson, rules);

        // Assert
        report.Errors.Should().NotBeEmpty();
        report.Findings.Should().ContainSingle(f =>
            f.RuleId == "R-PATH-TYPO" &&
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("repairQuantity"));
    }

    [Fact]
    public void Analyze_Should_Flag_Field_Casing_Mismatch_With_Suggestion()
    {
        // Arrange
        BusinessRule[] rules =
        [
            new()
            {
                Id = "R-CASING-1",
                Engine = "jsonlogic",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = JsonNode.Parse("""{">":[{"var":"siteStatus"},""]}""")
            },
            new()
            {
                Id = "R-CASING-2",
                Engine = "jsonlogic",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = JsonNode.Parse("""{">=":[{"var":"repairquantity"},0]}""")
            }
        ];

        // Act
        var report = _sut.Analyze(SampleSchemaJson, rules);

        // Assert
        report.Findings.Should().Contain(f =>
            f.RuleId == "R-CASING-2" &&
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("repairQuantity"));
    }

    [Fact]
    public void Analyze_Should_Flag_Enum_Literal_Mismatch_In_JsonLogic_And_Cel()
    {
        // Arrange
        BusinessRule[] rules =
        [
            new()
            {
                Id = "R-ENUM-JL",
                Engine = "jsonlogic",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = JsonNode.Parse("""{"==":[{"var":"siteStatus"},"open"]}""")
            },
            new()
            {
                Id = "R-ENUM-CEL",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.siteStatus in ['Open', 'Archived']"
            }
        ];

        // Act
        var report = _sut.Analyze(SampleSchemaJson, rules);

        // Assert
        report.Findings.Should().Contain(f =>
            f.RuleId == "R-ENUM-JL" &&
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("Did you mean 'Open'"));
        report.Findings.Should().Contain(f =>
            f.RuleId == "R-ENUM-CEL" &&
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("Archived"));
    }

    [Fact]
    public void Analyze_Should_Catch_Missing_Cel_Method_Receiver_Field_And_Validate_Comprehension_Loops()
    {
        // Arrange
        BusinessRule[] rules =
        [
            new()
            {
                Id = "R-CEL-METHOD-BAD",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.badField.contains('abc')"
            },
            new()
            {
                Id = "R-CEL-LOOP-OK",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.sites.all(s, s.siteName != '')"
            },
            new()
            {
                Id = "R-CEL-LOOP-BAD",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.sites.all(s, s.missingSiteProp != '')"
            }
        ];

        // Act
        var report = _sut.Analyze(SampleSchemaJson, rules);

        // Assert
        report.Findings.Should().Contain(f => f.RuleId == "R-CEL-METHOD-BAD" && f.Severity == RuleSeverity.Error);
        report.Findings.Should().NotContain(f => f.RuleId == "R-CEL-LOOP-OK");
        report.Findings.Should().Contain(f => f.RuleId == "R-CEL-LOOP-BAD" && f.Severity == RuleSeverity.Error);
    }
}
