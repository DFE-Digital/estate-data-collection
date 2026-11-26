using System.Diagnostics;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class UniqueCombinationEngineTests
{
    private static IReadOnlyList<BusinessRule> BuildUniqueRule() =>
    [
        new()
        {
            Id = "CCD-ELEM-V3-032",
            Type = "unique",
            Path = "elements[].occurrences[]",
            Severity = RuleSeverity.Error,
            Description = "Duplicate occurrence combination across productType, surveyLevel, location, conditionGrade",
            Fields = ["$parent.productType", "surveyLevel", "location", "conditionGrade"]
        }
    ];

    [Fact]
    public void ValidateRulesSyntax_Should_Resolve_Unique_Engine_For_Unique_RuleType()
    {
        // Arrange
        var coordinator = new QualityRuleCoordinator();
        const string rulesJson = """
        [
          {
            "id": "CCD-ELEM-V3-032",
            "type": "unique",
            "path": "elements[].occurrences[]",
            "description": "Composite uniqueness check",
            "fields": ["$parent.productType", "surveyLevel", "location", "conditionGrade"]
          }
        ]
        """;

        // Act
        var syntaxErrors = coordinator.ValidateRulesSyntax(rulesJson, out var parsedRules).ShouldSucceed();

        // Assert
        syntaxErrors.Should().BeEmpty();
        parsedRules.Should().ContainSingle(r => r.ResolveEngine("jsonlogic") == "unique");
    }

    [Fact]
    public async Task UniqueCombinationQualityEngine_Should_Detect_Composite_Duplicates_Across_Parents()
    {
        // Arrange
        const string duplicatePayload = """
        {
          "elements": [
            {
              "productType": "Roof",
              "occurrences": [
                { "surveyLevel": "Block", "location": "North Wing", "conditionGrade": "B" },
                { "surveyLevel": "Block", "location": "South Wing", "conditionGrade": "B" }
              ]
            },
            {
              "productType": "Roof",
              "occurrences": [
                { "surveyLevel": "Block", "location": "North Wing", "conditionGrade": "B" }
              ]
            },
            {
              "productType": "Boiler",
              "occurrences": [
                { "surveyLevel": "Block", "location": "North Wing", "conditionGrade": "B" }
              ]
            }
          ]
        }
        """;

        var rules = BuildUniqueRule();
        var coordinator = new QualityRuleCoordinator();

        // Act
        var result = (await coordinator.ValidateAsync(duplicatePayload, rules)).ShouldSucceed();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.RuleId == "CCD-ELEM-V3-032" &&
            e.EngineId == "unique" &&
            e.PropertyPath == "/elements/1/occurrences/0");
    }

    [Fact]
    public async Task UniqueCombinationQualityEngine_Should_Evaluate_Large_Dataset_In_Linear_Time()
    {
        // Arrange
        var largeOccurrences = new JsonArray();
        for (var i = 0; i < 2500; i++)
        {
            largeOccurrences.Add(new JsonObject
            {
                ["surveyLevel"] = "Block",
                ["location"] = $"Loc-{i}",
                ["conditionGrade"] = "B"
            });
        }

        var largePayload = new JsonObject
        {
            ["elements"] = new JsonArray(new JsonObject
            {
                ["productType"] = "Roof",
                ["occurrences"] = largeOccurrences
            })
        }.ToJsonString();

        var rules = BuildUniqueRule();
        var coordinator = new QualityRuleCoordinator();

        // Act
        var sw = Stopwatch.StartNew();
        var result = (await coordinator.ValidateAsync(largePayload, rules)).ShouldSucceed();
        sw.Stop();

        // Assert
        result.IsValid.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }
}
