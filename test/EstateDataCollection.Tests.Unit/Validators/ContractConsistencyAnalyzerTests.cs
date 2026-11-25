using AwesomeAssertions;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Validators;

public sealed class ContractConsistencyAnalyzerTests
{
    private readonly ContractConsistencyAnalyzer _sut = new();

    private const string SampleSchemaJson = """
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
        var rules = new[]
        {
            new BusinessRule
            {
                Id = "R-PATH-TYPO",
                Engine = "cel",
                Path = "repairQuantty",
                Severity = RuleSeverity.Error,
                Implementation = "this >= 0"
            }
        };

        var report = _sut.Analyze(SampleSchemaJson, rules);

        report.Errors.Should().NotBeEmpty();
        report.Findings.Should().ContainSingle(f =>
            f.RuleId == "R-PATH-TYPO" &&
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("repairQuantity"));
    }

    [Fact]
    public void Analyze_Should_Flag_Field_Casing_Mismatch_With_Suggestion()
    {
        var rules = new[]
        {
            new BusinessRule
            {
                Id = "R-CASING-1",
                Engine = "jsonlogic",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = System.Text.Json.Nodes.JsonNode.Parse("""{">":[{"var":"siteStatus"},""]}""")
            },
            new BusinessRule
            {
                Id = "R-CASING-2",
                Engine = "jsonlogic",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = System.Text.Json.Nodes.JsonNode.Parse("""{">=":[{"var":"repairquantity"},0]}""")
            }
        };

        var report = _sut.Analyze(SampleSchemaJson, rules);

        report.Findings.Should().Contain(f =>
            f.RuleId == "R-CASING-2" &&
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("repairQuantity"));
    }

    [Fact]
    public void Analyze_Should_Flag_Enum_Literal_Mismatch_In_JsonLogic_And_Cel()
    {
        var rules = new[]
        {
            new BusinessRule
            {
                Id = "R-ENUM-JL",
                Engine = "jsonlogic",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = System.Text.Json.Nodes.JsonNode.Parse("""{"==":[{"var":"siteStatus"},"open"]}""")
            },
            new BusinessRule
            {
                Id = "R-ENUM-CEL",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.siteStatus in ['Open', 'Archived']"
            }
        };

        var report = _sut.Analyze(SampleSchemaJson, rules);

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
        var rules = new[]
        {
            new BusinessRule
            {
                Id = "R-CEL-METHOD-BAD",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.badField.contains('abc')"
            },
            new BusinessRule
            {
                Id = "R-CEL-LOOP-OK",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.sites.all(s, s.siteName != '')"
            },
            new BusinessRule
            {
                Id = "R-CEL-LOOP-BAD",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.sites.all(s, s.missingSiteProp != '')"
            }
        };

        var report = _sut.Analyze(SampleSchemaJson, rules);

        report.Findings.Should().Contain(f => f.RuleId == "R-CEL-METHOD-BAD" && f.Severity == RuleSeverity.Error);
        report.Findings.Should().NotContain(f => f.RuleId == "R-CEL-LOOP-OK");
        report.Findings.Should().Contain(f => f.RuleId == "R-CEL-LOOP-BAD" && f.Severity == RuleSeverity.Error);
    }

    [Fact]
    public void Analyze_Should_Flag_DependsOn_Missing_Rule_Later_Stage_And_Cycles()
    {
        var rules = new[]
        {
            new BusinessRule
            {
                Id = "R-MISSING-DEP",
                Engine = "cel",
                Path = "/",
                Stage = 10,
                Severity = RuleSeverity.Error,
                DependsOn = ["R-DOES-NOT-EXIST"],
                Implementation = "this.urn > 0"
            },
            new BusinessRule
            {
                Id = "R-EARLY",
                Engine = "cel",
                Path = "/",
                Stage = 10,
                Severity = RuleSeverity.Error,
                DependsOn = ["R-LATE"],
                Implementation = "this.urn > 0"
            },
            new BusinessRule
            {
                Id = "R-LATE",
                Engine = "cel",
                Path = "/",
                Stage = 20,
                Severity = RuleSeverity.Error,
                DependsOn = ["R-EARLY"],
                Implementation = "this.urn > 0"
            }
        };

        var report = _sut.Analyze(SampleSchemaJson, rules);

        report.Findings.Should().Contain(f =>
            f.RuleId == "R-MISSING-DEP" &&
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("does not exist"));
        report.Findings.Should().Contain(f =>
            f.RuleId == "R-EARLY" &&
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("later Stage"));
        report.Findings.Should().Contain(f =>
            f.Severity == RuleSeverity.Error &&
            f.Message.Contains("Circular dependency cycle"));
    }

    [Fact]
    public void Analyze_Should_Emit_Warnings_For_Severity_Threshold_Inversions_And_Schema_Bound_Conflicts()
    {
        var rules = new[]
        {
            new BusinessRule
            {
                Id = "R-CRIT-STRICT",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Critical,
                Implementation = "this.repairQuantity >= 50"
            },
            new BusinessRule
            {
                Id = "R-WARN-LOOSE",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Warning,
                Implementation = "this.repairQuantity >= 20"
            },
            new BusinessRule
            {
                Id = "R-EXCEEDS-SCHEMA-MAX",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.repairQuantity >= 200"
            }
        };

        var report = _sut.Analyze(SampleSchemaJson, rules);

        report.Errors.Should().BeEmpty();
        report.Warnings.Should().NotBeEmpty();
        report.Findings.Should().Contain(f =>
            f.RuleId == "R-WARN-LOOSE" &&
            f.Severity == RuleSeverity.Warning &&
            f.Message.Contains("Threshold/severity inversion"));
        report.Findings.Should().Contain(f =>
            f.RuleId == "R-EXCEEDS-SCHEMA-MAX" &&
            f.Severity == RuleSeverity.Warning &&
            f.Message.Contains("maximum (100)"));
    }

    [Fact]
    public void Analyze_Should_Emit_Warning_For_Required_Vs_ConditionalOptional_Conflict_And_Validate_Unique_Fields()
    {
        var rules = new[]
        {
            new BusinessRule
            {
                Id = "R-COND-OPT-REQUIRED",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.siteStatus == 'Closed' || has(this.repairQuantity)"
            },
            new BusinessRule
            {
                Id = "R-UNIQUE-OK",
                Engine = "unique",
                Path = "elements[].occurrences[]",
                Severity = RuleSeverity.Error,
                Fields = ["$parent.productType", "surveyLevel", "conditionGrade"]
            },
            new BusinessRule
            {
                Id = "R-UNIQUE-BAD",
                Engine = "unique",
                Path = "elements[].occurrences[]",
                Severity = RuleSeverity.Error,
                Fields = ["nonExistentColumn"]
            }
        };

        var report = _sut.Analyze(SampleSchemaJson, rules);

        report.Findings.Should().Contain(f =>
            f.RuleId == "R-COND-OPT-REQUIRED" &&
            f.Severity == RuleSeverity.Warning &&
            f.Message.Contains("unconditionally marked 'required'"));
        report.Findings.Should().NotContain(f => f.RuleId == "R-UNIQUE-OK");
        report.Findings.Should().Contain(f =>
            f.RuleId == "R-UNIQUE-BAD" &&
            f.Severity == RuleSeverity.Error);
    }
}
