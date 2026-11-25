using AwesomeAssertions;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class BlobSchemaRegistryVerifyTests
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
        new(new JsonLogicValidator(), new OdcsContractParser());

    [Fact]
    public async Task VerifyCandidateSchema_Should_Validate_InFlight_Fixtures_For_Both_OdcsYaml_And_Split_Json()
    {
        var verifier = CreateVerifier();

        var odcsYaml = CdeContractFixtureLoader.LoadFile("contracts/edc-school-site-jsonlogic.odcs.yaml");
        var schemaJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json");
        var rulesJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.rules.json");

        var yamlOutcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: odcsYaml,
            rawRulesJson: null,
            sampleValidPayloads: [CdePayloadFixtures.ValidSchoolSitePayload],
            sampleInvalidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload])).ShouldSucceed();

        var jsonOutcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [CdePayloadFixtures.ValidSchoolSitePayload],
            sampleInvalidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload])).ShouldSucceed();

        var failedInFlight = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload],
            sampleInvalidPayloads: [])).ShouldSucceed();

        yamlOutcome.IsSuccessful.Should().BeTrue();
        yamlOutcome.Errors.Should().BeEmpty();
        yamlOutcome.Warnings.Should().ContainSingle(w => w.Message.Contains("unknown if expected"));

        jsonOutcome.IsSuccessful.Should().BeTrue();
        jsonOutcome.Errors.Should().BeEmpty();
        jsonOutcome.Warnings.Should().ContainSingle(w => w.Message.Contains("unknown if expected"));

        failedInFlight.IsSuccessful.Should().BeFalse();
        failedInFlight.FixtureErrors.Should().ContainSingle(e =>
            e.Identifier == "SampleValidFixtures:0" &&
            e.Message.Contains("SampleValidFixtures:0 violated quality rules") &&
            e.Message.Contains("/survey"));
    }

    [Fact]
    public async Task VerifyCandidateSchema_Should_Verify_Custom_LAEstab_Pointer_And_Fixtures()
    {
        var verifier = CreateVerifier();

        var idConfig = new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab" };

        var okOutcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: LaEstabSchema,
            rawRulesJson: null,
            sampleValidPayloads: ["""{"establishment":{"laestab":"201-3614"}}"""],
            sampleInvalidPayloads: ["""{"establishment":{"laestab":""}}"""],
            identifierConfig: idConfig)).ShouldSucceed();

        okOutcome.IsSuccessful.Should().BeTrue();
        okOutcome.ResolvedIdentifierConfig.Should().NotBeNull();
        okOutcome.ResolvedIdentifierConfig!.Type.Should().Be("LAEstab");
        okOutcome.ResolvedIdentifierConfig.JsonPointer.Should().Be("/establishment/laestab");
        okOutcome.ResolvedIdentifierConfig.Required.Should().BeTrue();

        var missingDefaultOutcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: LaEstabSchema,
            rawRulesJson: null,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [],
            identifierConfig: null)).ShouldSucceed();

        missingDefaultOutcome.IsSuccessful.Should().BeFalse();
        missingDefaultOutcome.SyntaxErrors.Should().Contain(e => e.Identifier == "identifierConfig.jsonPointer");
    }

    [Fact]
    public async Task VerifyCandidateSchema_Should_Respect_Optional_Schema_Identifier_And_Flag_NonPositive_Urn_Fixtures()
    {
        var verifier = CreateVerifier();

        var idConfig = new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" };

        var outcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: OptionalUrnSchema,
            rawRulesJson: null,
            sampleValidPayloads:
            [
                """{"surveyName":"Condition"}""",
                """{"urn":104521,"surveyName":"Condition"}"""
            ],
            sampleInvalidPayloads:
            [
                """{"urn":0,"surveyName":"Condition"}"""
            ],
            identifierConfig: idConfig)).ShouldSucceed();

        outcome.IsSuccessful.Should().BeTrue();
        outcome.ResolvedIdentifierConfig!.Required.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCandidateSchema_Should_Capture_InvalidFixtureOutcomes_SoftWarnings_And_Validate_ExpectedErrors()
    {
        var verifier = CreateVerifier();

        var rulesJson = """
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

        var schemaJson = """
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

        var matchingInvalid = """
        {
          "description": "Breaks minimum capacity rule",
          "payload": { "urn": 123456, "capacity": 5 },
          "expectedErrors": ["RULE-CAPACITY-MIN"]
        }
        """;

        var okOutcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: ["""{"urn":123456,"capacity":20}"""],
            sampleInvalidPayloads: [matchingInvalid],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        okOutcome.IsSuccessful.Should().BeTrue();
        okOutcome.Warnings.Should().BeEmpty();
        var outcome = okOutcome.InvalidFixtureOutcomes.Should().ContainSingle().Subject;
        outcome.FailedAsExpected.Should().BeTrue();
        outcome.Description.Should().Be("Breaks minimum capacity rule");
        outcome.TriggeredErrors.Should().ContainSingle(e =>
            e.Source == ValidationSources.Rule &&
            e.RuleId == "RULE-CAPACITY-MIN" &&
            e.Message.Contains("Capacity must be at least 10."));
        outcome.UnmatchedExpectedErrors.Should().BeEmpty();
        outcome.Warning.Should().BeNull();

        // Raw invalid fixture without expectedErrors -> passes verification with a soft warning AND returns triggered errors
        var rawInvalidWithoutExpectations = """{"urn":123456,"capacity":5}""";
        var softWarningOutcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: ["""{"urn":123456,"capacity":20}"""],
            sampleInvalidPayloads: [rawInvalidWithoutExpectations],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        softWarningOutcome.IsSuccessful.Should().BeTrue();
        softWarningOutcome.Warnings.Should().ContainSingle(w =>
            w.Identifier == "SampleInvalidFixtures:0" &&
            w.Message.Contains("unknown if expected") &&
            w.Message.Contains("RULE-CAPACITY-MIN"));
        var softOutcome = softWarningOutcome.InvalidFixtureOutcomes.Should().ContainSingle().Subject;
        softOutcome.FailedAsExpected.Should().BeTrue();
        softOutcome.Warning.Should().Contain("unknown if expected");
        softOutcome.TriggeredErrors.Should().ContainSingle(e =>
            e.Source == ValidationSources.Rule &&
            e.RuleId == "RULE-CAPACITY-MIN");

        var mismatchedInvalid = """
        {
          "description": "Wrong expected rule",
          "payload": { "urn": 123456, "capacity": 5 },
          "expectedErrors": ["RULE-DIFFERENT"]
        }
        """;

        var mismatchOutcome = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [mismatchedInvalid],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        mismatchOutcome.IsSuccessful.Should().BeFalse();
        mismatchOutcome.FixtureErrors.Should().ContainSingle(e =>
            e.Identifier == "SampleInvalidFixtures:0" &&
            e.Message.Contains("did not trigger expected error(s) [RULE-DIFFERENT]") &&
            e.Message.Contains("RULE-CAPACITY-MIN"));
        mismatchOutcome.InvalidFixtureOutcomes!.Single().UnmatchedExpectedErrors.Should().BeEquivalentTo(["RULE-DIFFERENT"]);
    }
}
