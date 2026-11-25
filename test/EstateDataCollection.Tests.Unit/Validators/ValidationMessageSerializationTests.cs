using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Validators;

public sealed class ValidationMessageSerializationTests
{
    [Fact]
    public async Task VerifyCandidateSchemaAsync_Should_Populate_Structured_Provenance_And_Metadata_For_Grouping()
    {
        // Arrange
        var sut = new SchemaVerificationService(
            new QualityRuleCoordinator(),
            new OdcsContractParser(),
            schemaValidator: new JsonSchemaNetValidator());

        const string schemaJson = """
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

        const string rulesJson = """
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

        const string multiSourceInvalid = """{"capacity":"not-an-int"}""";

        // Act
        var result = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [multiSourceInvalid],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        var outcome = result.InvalidFixtureOutcomes.Should().ContainSingle().Subject;

        var bySource = outcome.TriggeredErrors.GroupBy(e => e.Source).ToDictionary(g => g.Key!, g => g.ToList());
        bySource.Should().ContainKeys(ValidationSources.Schema, ValidationSources.Rule, ValidationSources.Identifier);

        var schemaError = bySource[ValidationSources.Schema].Should().ContainSingle(e => e.Path == "/capacity").Subject;
        schemaError.Metadata.Should().ContainKey("keyword");

        var ruleError = bySource[ValidationSources.Rule].Should().ContainSingle().Subject;
        ruleError.RuleId.Should().Be("RULE-CAPACITY-MIN");
        ruleError.Path.Should().Be("/");
        ruleError.Message.Should().Be("Capacity must be at least 10.");
        ruleError.FormatDisplay().Should().Be("[Rule:RULE-CAPACITY-MIN] /: Capacity must be at least 10.");

        var idError = bySource[ValidationSources.Identifier].Should().ContainSingle().Subject;
        idError.Path.Should().Be("/urn");
        idError.Metadata.Should().ContainKey("identifierType");
        idError.Metadata!["identifierType"].Should().Be("URN");

        result.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema").Summary
            .Should().Contain("triggered 1 invalid fixture");
        result.EngineTraces!.Single(t => t.Descriptor.EngineId == "jsonlogic").Summary
            .Should().Contain("triggered 1 invalid fixture");
        result.EngineTraces!.Single(t => t.Descriptor.EngineId == "identifier-extractor").Summary
            .Should().Contain("triggered 1 invalid fixture");
    }

    [Fact]
    public void ValidationMessage_JsonSerialisation_Should_RoundTrip_Structured_Entries()
    {
        // Arrange
        var structured = ValidationMessage.For(
            ValidationSources.Rule,
            "Capacity must be at least 10.",
            identifier: "SampleInvalidFixtures:0",
            path: "/capacity",
            ruleId: "RULE-CAPACITY-MIN",
            metadata: new Dictionary<string, object?>
            {
                ["severity"] = "ERROR",
                ["attempt"] = 2
            });

        // Act
        var json = JsonSerializer.Serialize(structured);
        var roundTripped = JsonSerializer.Deserialize<ValidationMessage>(json);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.Source.Should().Be(ValidationSources.Rule);
        roundTripped.Identifier.Should().Be("SampleInvalidFixtures:0");
        roundTripped.Path.Should().Be("/capacity");
        roundTripped.RuleId.Should().Be("RULE-CAPACITY-MIN");
        roundTripped.Message.Should().Be("Capacity must be at least 10.");
        roundTripped.Metadata.Should().ContainKeys("severity", "attempt");
    }

    [Fact]
    public void ValidationMessage_JsonDeserialisation_Should_Support_Legacy_String_Entries()
    {
        // Arrange
        const string legacyJson = """["[Rule:RULE-CAPACITY-MIN] /capacity: Capacity must be at least 10.","Plain error"]""";

        // Act
        var parsedLegacy = JsonSerializer.Deserialize<List<ValidationMessage>>(legacyJson);

        // Assert
        parsedLegacy.Should().NotBeNull().And.HaveCount(2);
        parsedLegacy![0].Source.Should().Be("Rule");
        parsedLegacy[0].RuleId.Should().Be("RULE-CAPACITY-MIN");
        parsedLegacy[0].Path.Should().Be("/capacity");
        parsedLegacy[0].Message.Should().Be("Capacity must be at least 10.");

        parsedLegacy[1].Source.Should().BeNull();
        parsedLegacy[1].Message.Should().Be("Plain error");
    }
}
