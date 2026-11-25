using AwesomeAssertions;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class SchemaVerificationFixtureTests
{
    private const string LaEstabSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["establishment"],
      "properties": {
        "establishment": {
          "type": "object",
          "required": ["laestab"],
          "properties": {
            "laestab": { "type": "string" }
          }
        }
      }
    }
    """;

    private const string OptionalUrnSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["surveyName"],
      "properties": {
        "urn": { "type": "integer", "minimum": 1 },
        "surveyName": { "type": "string" }
      }
    }
    """;

    private static SchemaVerificationService CreateVerifier() =>
        new(new QualityRuleCoordinator(), new OdcsContractParser());

    [Fact]
    public async Task VerifyCandidateSchema_Should_Validate_InFlight_Fixtures_For_OdcsYaml()
    {
        // Arrange
        var verifier = CreateVerifier();
        var odcsYaml = EdcSchoolSiteContractBuilder.DefaultOdcsYaml;

        // Act
        var outcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: odcsYaml,
            rawRulesJson: null,
            sampleValidPayloads: [CdePayloadFixtures.ValidSchoolSitePayload],
            sampleInvalidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload])).ShouldSucceed();

        // Assert
        outcome.IsSuccessful.Should().BeTrue();
        outcome.Errors.Should().BeEmpty();
        outcome.Warnings.Should().ContainSingle(w => w.Message.Contains("unknown if expected"));
    }

    [Fact]
    public async Task VerifyCandidateSchema_Should_Validate_InFlight_Fixtures_For_Split_Json()
    {
        // Arrange
        var verifier = CreateVerifier();
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rulesJson = EdcSchoolSiteContractBuilder.DefaultRulesJson;

        // Act
        var outcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [CdePayloadFixtures.ValidSchoolSitePayload],
            sampleInvalidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload])).ShouldSucceed();

        // Assert
        outcome.IsSuccessful.Should().BeTrue();
        outcome.Errors.Should().BeEmpty();
        outcome.Warnings.Should().ContainSingle(w => w.Message.Contains("unknown if expected"));
    }

    [Fact]
    public async Task VerifyCandidateSchema_When_ValidFixture_Violates_Rules_Should_Return_FixtureError()
    {
        // Arrange
        var verifier = CreateVerifier();
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rulesJson = EdcSchoolSiteContractBuilder.DefaultRulesJson;

        // Act
        var outcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload],
            sampleInvalidPayloads: [])).ShouldSucceed();

        // Assert
        outcome.IsSuccessful.Should().BeFalse();
        outcome.FixtureErrors.Should().ContainSingle(e =>
            e.Identifier == "SampleValidFixtures:0" &&
            e.Message.Contains("SampleValidFixtures:0 violated quality rules") &&
            e.Message.Contains("/survey"));
    }

    [Fact]
    public async Task VerifyCandidateSchema_Should_Verify_Custom_LAEstab_Pointer_And_Fixtures()
    {
        // Arrange
        var verifier = CreateVerifier();
        var idConfig = new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab" };

        // Act
        var outcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: LaEstabSchema,
            rawRulesJson: null,
            sampleValidPayloads: ["""{"establishment":{"laestab":"201-3614"}}"""],
            sampleInvalidPayloads: ["""{"establishment":{"laestab":""}}"""],
            identifierConfig: idConfig)).ShouldSucceed();

        // Assert
        outcome.IsSuccessful.Should().BeTrue();
        outcome.ResolvedIdentifierConfig.Should().NotBeNull();
        outcome.ResolvedIdentifierConfig!.Type.Should().Be("LAEstab");
        outcome.ResolvedIdentifierConfig.JsonPointer.Should().Be("/establishment/laestab");
        outcome.ResolvedIdentifierConfig.Required.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyCandidateSchema_When_Default_IdentifierPointer_Missing_Should_Report_SyntaxError()
    {
        // Arrange
        var verifier = CreateVerifier();

        // Act
        var outcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: LaEstabSchema,
            rawRulesJson: null,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [],
            identifierConfig: null)).ShouldSucceed();

        // Assert
        outcome.IsSuccessful.Should().BeFalse();
        outcome.SyntaxErrors.Should().Contain(e => e.Identifier == "identifierConfig.jsonPointer");
    }

    [Fact]
    public async Task VerifyCandidateSchema_Should_Respect_Optional_Schema_Identifier_And_Flag_NonPositive_Urn_Fixtures()
    {
        // Arrange
        var verifier = CreateVerifier();
        var idConfig = new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" };

        // Act
        var outcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: OptionalUrnSchema,
            rawRulesJson: null,
            sampleValidPayloads:
            [
                """{"surveyName":"Condition"}""",
                """{"urn":104521,"surveyName":"Condition"}"""
            ],
            sampleInvalidPayloads: ["""{"urn":0,"surveyName":"Condition"}"""],
            identifierConfig: idConfig)).ShouldSucceed();

        // Assert
        outcome.IsSuccessful.Should().BeTrue();
        outcome.ResolvedIdentifierConfig!.Required.Should().BeFalse();
    }
}
