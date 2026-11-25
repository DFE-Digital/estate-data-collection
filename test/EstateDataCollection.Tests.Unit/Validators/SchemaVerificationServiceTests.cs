using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Validators;

public sealed class SchemaVerificationServiceTests
{
    private static SchemaVerificationService CreateService()
    {
        return new SchemaVerificationService(
            new JsonLogicValidator(),
            new OdcsContractParser(),
            schemaValidator: new JsonSchemaNetValidator());
    }

    [Fact]
    public async Task VerifyCandidateSchemaAsync_Should_Verify_Schema_And_Rules_Without_Blob_Storage()
    {
        var sut = CreateService();
        var odcsYaml = CdeContractFixtureLoader.LoadFile("contracts/edc-school-site-jsonlogic.odcs.yaml");

        var result = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: odcsYaml,
            rawRulesJson: null,
            sampleValidPayloads: [CdePayloadFixtures.ValidSchoolSitePayload],
            sampleInvalidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload])).ShouldSucceed();

        result.IsSuccessful.Should().BeTrue();
        result.SyntaxErrors.Should().BeEmpty();
        result.FixtureErrors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Message.Contains("unknown if expected"));
        result.EngineTraces.Should().NotBeNull();
        result.EngineTraces!.Select(t => t.Descriptor.EngineId).Should().BeEquivalentTo(
            ["json-schema", "jsonlogic", "cel", "unique", "identifier-extractor", "contract-consistency-analyzer"]);
        result.EngineTraces.Single(t => t.Descriptor.EngineId == "json-schema").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        result.EngineTraces.Single(t => t.Descriptor.EngineId == "jsonlogic").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        result.EngineTraces.Single(t => t.Descriptor.EngineId == "cel").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
    }

    [Fact]
    public async Task VerifyCandidateSchemaAsync_When_Inputs_Empty_Should_Return_Syntax_Error()
    {
        var sut = CreateService();

        var result = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: null,
            rawRulesJson: null,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [])).ShouldSucceed();

        result.IsSuccessful.Should().BeFalse();
        result.SyntaxErrors.Should().ContainSingle(e => e.Identifier == "schema");
    }

    [Fact]
    public async Task VerifyCandidateSchemaAsync_Should_Report_Per_Engine_Diagnostics_For_Failures_And_Invalid_Triggers()
    {
        var sut = CreateService();

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
            "id": "RULE-CAPACITY-CEL",
            "description": "Capacity >= 10",
            "severity": "ERROR",
            "engine": "cel",
            "path": "/",
            "message": "Capacity must be at least 10.",
            "assert": "this.capacity >= 10"
          }
        ]
        """;

        // Failing valid fixture on CEL rule should mark cel trace ValidationErrorsFound while json-schema succeeds and jsonlogic is Skipped.
        var failedValid = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: ["""{"urn":123456,"capacity":5}"""],
            sampleInvalidPayloads: [],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        failedValid.IsSuccessful.Should().BeFalse();
        failedValid.EngineTraces.Should().NotBeNull();
        failedValid.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        failedValid.EngineTraces.Single(t => t.Descriptor.EngineId == "jsonlogic").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));

        var celFailTrace = failedValid.EngineTraces.Single(t => t.Descriptor.EngineId == "cel");
        celFailTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.ValidationErrorsFound));
        celFailTrace.ViolationCount.Should().Be(1);

        // Malformed schema syntax should mark json-schema as Failed.
        var badSyntax = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: "{broken",
            rawRulesJson: null,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [])).ShouldSucceed();

        badSyntax.IsSuccessful.Should().BeFalse();
        var schemaFailTrace = badSyntax.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema");
        schemaFailTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.Failed));
        schemaFailTrace.ViolationCount.Should().Be(1);
    }

    [Fact]
    public async Task VerifySchemaUseCase_Should_Delegate_To_SchemaVerificationService()
    {
        var verifier = Substitute.For<ISchemaVerificationService>();
        verifier.VerifyCandidateSchemaAsync(
                JsonSchemaFixtures.ValidV1Schema,
                null,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<SchemaIdentifierConfig?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [])));

        var useCase = new VerifySchemaUseCase(verifier);

        var result = await useCase.ExecuteAsync(new VerifySchemaRequest(
            JsonSchemaFixtures.ValidV1Schema,
            [SurveyPayloadFixtures.ValidPayloadJson],
            [SurveyPayloadFixtures.MissingUrnJson]));

        result.IsSuccess.Should().BeTrue();
        await verifier.Received(1).VerifyCandidateSchemaAsync(
            JsonSchemaFixtures.ValidV1Schema,
            null,
            Arg.Is<IReadOnlyList<string>>(v => v.Count == 1),
            Arg.Is<IReadOnlyList<string>>(i => i.Count == 1),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCandidateSchemaAsync_Should_Populate_Structured_Provenance_And_Metadata_For_Grouping()
    {
        var sut = CreateService();

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

        var result = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [multiSourceInvalid],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

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
    public void ValidationMessage_JsonSerialisation_Should_RoundTrip_Structured_And_Legacy_Strings()
    {
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

        var json = JsonSerializer.Serialize(structured);
        var roundTripped = JsonSerializer.Deserialize<ValidationMessage>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.Source.Should().Be(ValidationSources.Rule);
        roundTripped.Identifier.Should().Be("SampleInvalidFixtures:0");
        roundTripped.Path.Should().Be("/capacity");
        roundTripped.RuleId.Should().Be("RULE-CAPACITY-MIN");
        roundTripped.Message.Should().Be("Capacity must be at least 10.");
        roundTripped.Metadata.Should().ContainKeys("severity", "attempt");

        var legacyJson = """["[Rule:RULE-CAPACITY-MIN] /capacity: Capacity must be at least 10.","Plain error"]""";
        var parsedLegacy = JsonSerializer.Deserialize<List<ValidationMessage>>(legacyJson);

        parsedLegacy.Should().NotBeNull().And.HaveCount(2);
        parsedLegacy![0].Source.Should().Be("Rule");
        parsedLegacy[0].RuleId.Should().Be("RULE-CAPACITY-MIN");
        parsedLegacy[0].Path.Should().Be("/capacity");
        parsedLegacy[0].Message.Should().Be("Capacity must be at least 10.");

        parsedLegacy[1].Source.Should().BeNull();
        parsedLegacy[1].Message.Should().Be("Plain error");
    }
}
