using AwesomeAssertions;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Validators;

public sealed class ContractConsistencyCrossRuleTests
{
    private readonly ContractConsistencyAnalyzer _sut = new();

    [Fact]
    public void Analyze_Should_Flag_DependsOn_Missing_Rule_Later_Stage_And_Cycles()
    {
        // Arrange
        BusinessRule[] rules =
        [
            new()
            {
                Id = "R-MISSING-DEP",
                Engine = "cel",
                Path = "/",
                Stage = 10,
                Severity = RuleSeverity.Error,
                DependsOn = ["R-DOES-NOT-EXIST"],
                Implementation = "this.urn > 0"
            },
            new()
            {
                Id = "R-EARLY",
                Engine = "cel",
                Path = "/",
                Stage = 10,
                Severity = RuleSeverity.Error,
                DependsOn = ["R-LATE"],
                Implementation = "this.urn > 0"
            },
            new()
            {
                Id = "R-LATE",
                Engine = "cel",
                Path = "/",
                Stage = 20,
                Severity = RuleSeverity.Error,
                DependsOn = ["R-EARLY"],
                Implementation = "this.urn > 0"
            }
        ];

        // Act
        var report = _sut.Analyze(ContractConsistencyPathAndEnumTests.SampleSchemaJson, rules);

        // Assert
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
        // Arrange
        BusinessRule[] rules =
        [
            new()
            {
                Id = "R-CRIT-STRICT",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Critical,
                Implementation = "this.repairQuantity >= 50"
            },
            new()
            {
                Id = "R-WARN-LOOSE",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Warning,
                Implementation = "this.repairQuantity >= 20"
            },
            new()
            {
                Id = "R-EXCEEDS-SCHEMA-MAX",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.repairQuantity >= 200"
            }
        ];

        // Act
        var report = _sut.Analyze(ContractConsistencyPathAndEnumTests.SampleSchemaJson, rules);

        // Assert
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
        // Arrange
        BusinessRule[] rules =
        [
            new()
            {
                Id = "R-COND-OPT-REQUIRED",
                Engine = "cel",
                Path = "/",
                Severity = RuleSeverity.Error,
                Implementation = "this.siteStatus == 'Closed' || has(this.repairQuantity)"
            },
            new()
            {
                Id = "R-UNIQUE-OK",
                Engine = "unique",
                Path = "elements[].occurrences[]",
                Severity = RuleSeverity.Error,
                Fields = ["$parent.productType", "surveyLevel", "conditionGrade"]
            },
            new()
            {
                Id = "R-UNIQUE-BAD",
                Engine = "unique",
                Path = "elements[].occurrences[]",
                Severity = RuleSeverity.Error,
                Fields = ["nonExistentColumn"]
            }
        ];

        // Act
        var report = _sut.Analyze(ContractConsistencyPathAndEnumTests.SampleSchemaJson, rules);

        // Assert
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
