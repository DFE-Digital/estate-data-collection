using AwesomeAssertions;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class SchemaVerificationExpectedErrorTests
{
    private const string SchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["urn", "capacity"],
      "properties": {
        "urn": { "type": "integer" },
        "capacity": { "type": "integer" }
      }
    }
    """;

    private const string RulesJson = """
    [
      {
        "id": "RULE-CAPACITY-MIN",
        "description": "Capacity >= 10",
        "severity": "ERROR",
        "path": "/",
        "message": "Capacity must be at least 10.",
        "assert": { ">=": [ { "var": "capacity" }, 10 ] }
      }
    ]
    """;

    private static SchemaVerificationService CreateVerifier() =>
        new(new QualityRuleCoordinator(), new OdcsContractParser());

    [Fact]
    public async Task VerifyCandidateSchema_When_InvalidFixture_Matches_ExpectedErrors_Should_Pass_Without_Warnings()
    {
        // Arrange
        var verifier = CreateVerifier();
        const string matchingInvalid = """
        {
          "description": "Breaks minimum capacity rule",
          "payload": { "urn": 123456, "capacity": 5 },
          "expectedErrors": ["RULE-CAPACITY-MIN"]
        }
        """;

        // Act
        var result = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: SchemaJson,
            rawRulesJson: RulesJson,
            sampleValidPayloads: ["""{"urn":123456,"capacity":20}"""],
            sampleInvalidPayloads: [matchingInvalid],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        var outcome = result.InvalidFixtureOutcomes.Should().ContainSingle().Subject;
        outcome.FailedAsExpected.Should().BeTrue();
        outcome.Description.Should().Be("Breaks minimum capacity rule");
        outcome.TriggeredErrors.Should().ContainSingle(e =>
            e.Source == ValidationSources.Rule &&
            e.RuleId == "RULE-CAPACITY-MIN" &&
            e.Message.Contains("Capacity must be at least 10."));
        outcome.UnmatchedExpectedErrors.Should().BeEmpty();
        outcome.Warning.Should().BeNull();
    }

    [Fact]
    public async Task VerifyCandidateSchema_When_InvalidFixture_Omits_ExpectedErrors_Should_Emit_SoftWarning()
    {
        // Arrange
        var verifier = CreateVerifier();
        const string rawInvalidWithoutExpectations = """{"urn":123456,"capacity":5}""";

        // Act
        var result = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: SchemaJson,
            rawRulesJson: RulesJson,
            sampleValidPayloads: ["""{"urn":123456,"capacity":20}"""],
            sampleInvalidPayloads: [rawInvalidWithoutExpectations],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w =>
            w.Identifier == "SampleInvalidFixtures:0" &&
            w.Message.Contains("unknown if expected") &&
            w.Message.Contains("RULE-CAPACITY-MIN"));
        var softOutcome = result.InvalidFixtureOutcomes.Should().ContainSingle().Subject;
        softOutcome.FailedAsExpected.Should().BeTrue();
        softOutcome.Warning.Should().Contain("unknown if expected");
        softOutcome.TriggeredErrors.Should().ContainSingle(e =>
            e.Source == ValidationSources.Rule &&
            e.RuleId == "RULE-CAPACITY-MIN");
    }

    [Fact]
    public async Task VerifyCandidateSchema_When_InvalidFixture_Mismatches_ExpectedErrors_Should_Fail_Verification()
    {
        // Arrange
        var verifier = CreateVerifier();
        const string mismatchedInvalid = """
        {
          "description": "Wrong expected rule",
          "payload": { "urn": 123456, "capacity": 5 },
          "expectedErrors": ["RULE-DIFFERENT"]
        }
        """;

        // Act
        var result = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: SchemaJson,
            rawRulesJson: RulesJson,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [mismatchedInvalid],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.FixtureErrors.Should().ContainSingle(e =>
            e.Identifier == "SampleInvalidFixtures:0" &&
            e.Message.Contains("did not trigger expected error(s) [RULE-DIFFERENT]") &&
            e.Message.Contains("RULE-CAPACITY-MIN"));
        result.InvalidFixtureOutcomes!.Single().UnmatchedExpectedErrors.Should().BeEquivalentTo(["RULE-DIFFERENT"]);
    }
}
