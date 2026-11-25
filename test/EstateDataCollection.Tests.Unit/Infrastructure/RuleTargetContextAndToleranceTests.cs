using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class RuleTargetContextAndToleranceTests
{
    private const string FloatArtifactPayload = """
    {
      "a": 0.1,
      "b": 0.2,
      "c": 0.30000000000000004,
      "exactSum": 0.3,
      "qty": 3,
      "unitRate": 33.333333333333336,
      "total": 100.0,
      "diffTiny": 0.001,
      "zero": 0.0
    }
    """;

    [Fact]
    public async Task RuleTargetResolver_Should_Inject_Parent_Ancestors_Root_And_Index_For_JsonLogic_And_Cel()
    {
        // Arrange
        const string nestedPayload = """
        {
          "survey": { "year": 2026 },
          "elements": [
            {
              "productType": "Boiler",
              "occurrences": [
                {
                  "conditionGrade": "D",
                  "replacement": { "replacementQuantity": 5 },
                  "repairs": [
                    { "repairItem": "Boiler Valve", "repairQuantity": 2 },
                    { "repairItem": "Roof Tile", "repairQuantity": 6 }
                  ]
                }
              ]
            }
          ]
        }
        """;

        IReadOnlyList<BusinessRule> rules =
        [
            new()
            {
                Id = "JL-PARENT-ANCESTOR",
                Engine = "jsonlogic",
                Path = "elements[].occurrences[].repairs[]",
                Severity = RuleSeverity.Error,
                Description = "Repair item must match grandparent productType (JsonLogic)",
                Implementation = JsonNode.Parse("""
                {
                  "==": [
                    { "var": "$ancestors.0.productType" },
                    { "var": "$parent.$parent.productType" }
                  ]
                }
                """)
            },
            new()
            {
                Id = "CEL-GRANDPARENT-MATCH",
                Engine = "cel",
                Path = "elements[].occurrences[].repairs[]",
                Severity = RuleSeverity.Error,
                Description = "Repair item must start with grandparent productType (CEL)",
                Implementation = JsonValue.Create("this.repairItem.startsWith($parent.$parent.productType) && $index >= 0 && $root.survey.year == 2026")
            },
            new()
            {
                Id = "CEL-PARENT-QUANTITY",
                Engine = "cel",
                Path = "elements[].occurrences[].repairs[]",
                Severity = RuleSeverity.Error,
                Description = "Parent replacementQuantity must be >= repairQuantity",
                Implementation = JsonValue.Create("$parent.replacement.replacementQuantity >= this.repairQuantity")
            }
        ];

        var coordinator = new QualityRuleCoordinator();

        // Act
        var result = (await coordinator.ValidateAsync(nestedPayload, rules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().OnlyContain(e => e.PropertyPath == "/elements/0/occurrences/0/repairs/1");
        result.Errors.Select(e => e.RuleId).Should().BeEquivalentTo(["CEL-GRANDPARENT-MATCH", "CEL-PARENT-QUANTITY"]);
    }

    [Fact]
    public async Task RuleTargetResolver_Should_Prevent_Payload_Spoofing_Of_Parent_Metadata()
    {
        // Arrange
        const string spoofedPayload = """
        {
          "elements": [
            {
              "productType": "Window",
              "occurrences": [
                {
                  "repairs": [
                    {
                      "$parent": { "$parent": { "productType": "Boiler" } },
                      "repairItem": "Boiler Valve"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        IReadOnlyList<BusinessRule> rules =
        [
            new()
            {
                Id = "CEL-ANTI-SPOOF",
                Engine = "cel",
                Path = "elements[].occurrences[].repairs[]",
                Severity = RuleSeverity.Error,
                Description = "Grandparent productType is resolved structurally",
                Implementation = JsonValue.Create("this.repairItem.startsWith($parent.$parent.productType)")
            }
        ];

        var coordinator = new QualityRuleCoordinator();

        // Act
        var result = (await coordinator.ValidateAsync(spoofedPayload, rules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.RuleId == "CEL-ANTI-SPOOF");
    }

    [Fact]
    public async Task JsonLogic_And_Cel_Should_Absorb_Float_Epsilon_Rounding_Artifacts()
    {
        // Arrange
        IReadOnlyList<BusinessRule> passingRules =
        [
            new()
            {
                Id = "JL-FLOAT-SUM",
                Engine = "jsonlogic",
                Path = "/",
                Description = "JsonLogic absorbs IEEE-754 sum artifact",
                Implementation = JsonNode.Parse("""{"==":[{"+":[{"var":"a"},{"var":"b"}]},{"var":"c"}]}""")
            },
            new()
            {
                Id = "JL-FLOAT-MUL",
                Engine = "jsonlogic",
                Path = "/",
                Description = "JsonLogic absorbs 1/3 * 3 rounding noise",
                Implementation = JsonNode.Parse("""{"==":[{"*":[{"var":"qty"},{"var":"unitRate"}]},{"var":"total"}]}""")
            },
            new()
            {
                Id = "CEL-FLOAT-SUM",
                Engine = "cel",
                Path = "/",
                Description = "CEL absorbs 0.1 + 0.2 == 0.3",
                Implementation = JsonValue.Create("this.a + this.b == this.exactSum && this.qty * this.unitRate == this.total")
            }
        ];

        var coordinator = new QualityRuleCoordinator();

        // Act
        var result = (await coordinator.ValidateAsync(FloatArtifactPayload, passingRules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task JsonLogic_And_Cel_Should_Reject_Real_Numeric_Differences_Beyond_Default_Epsilon()
    {
        // Arrange
        IReadOnlyList<BusinessRule> failingRules =
        [
            new()
            {
                Id = "JL-REAL-MISMATCH",
                Engine = "jsonlogic",
                Path = "/",
                Description = "0.001 != 0",
                Implementation = JsonNode.Parse("""{"==":[{"var":"diffTiny"},{"var":"zero"}]}""")
            },
            new()
            {
                Id = "CEL-REAL-MISMATCH",
                Engine = "cel",
                Path = "/",
                Description = "CEL 0.001 != 0",
                Implementation = JsonValue.Create("this.diffTiny == this.zero")
            }
        ];

        var coordinator = new QualityRuleCoordinator();

        // Act
        var result = (await coordinator.ValidateAsync(FloatArtifactPayload, failingRules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors!.Select(e => e.RuleId).Should().BeEquivalentTo(["JL-REAL-MISMATCH", "CEL-REAL-MISMATCH"]);
    }

    [Fact]
    public async Task NumericToleranceOverride_Should_Allow_Small_Difference_Within_Override_Band()
    {
        // Arrange
        IReadOnlyList<BusinessRule> rules =
        [
            new()
            {
                Id = "JL-REAL-MISMATCH",
                Engine = "jsonlogic",
                Path = "/",
                Description = "0.001 == 0 within 0.005 override",
                Implementation = JsonNode.Parse("""{"==":[{"var":"diffTiny"},{"var":"zero"}]}""")
            },
            new()
            {
                Id = "CEL-REAL-MISMATCH",
                Engine = "cel",
                Path = "/",
                Description = "CEL 0.001 == 0 within 0.005 override",
                Implementation = JsonValue.Create("this.diffTiny == this.zero")
            }
        ];

        var coordinator = new QualityRuleCoordinator();

        // Act
        var result = (await coordinator.ValidateAsync(
            FloatArtifactPayload,
            rules,
            additionalAmbientContext: null,
            numericToleranceOverride: 0.005)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
