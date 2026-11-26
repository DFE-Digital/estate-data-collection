using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Validators;

public sealed class SchemaVerificationServiceTests
{
    private static SchemaVerificationService CreateService() =>
        new(
            new QualityRuleCoordinator(),
            new OdcsContractParser(),
            schemaValidator: new JsonSchemaNetValidator());

    [Fact]
    public async Task VerifyCandidateSchemaAsync_Should_Verify_Schema_And_Rules_Without_Blob_Storage()
    {
        // Arrange
        var sut = CreateService();
        var odcsYaml = EdcSchoolSiteContractBuilder.DefaultOdcsYaml;

        // Act
        var result = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: odcsYaml,
            rawRulesJson: null,
            sampleValidPayloads: [CdePayloadFixtures.ValidSchoolSitePayload],
            sampleInvalidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload])).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.SyntaxErrors.Should().BeEmpty();
        result.FixtureErrors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Message.Contains("unknown if expected"));
        result.EngineTraces.Should().NotBeNull();
        result.EngineTraces!.Select(t => t.Descriptor.EngineId).Should().BeEquivalentTo(
            ["json-schema", "jsonlogic", "cel", "unique", "identifier-extractor", "contract-consistency-analyser"]);
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
        // Arrange
        var sut = CreateService();

        // Act
        var result = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: null,
            rawRulesJson: null,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [])).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.SyntaxErrors.Should().ContainSingle(e => e.Identifier == "schema");
    }

    [Fact]
    public async Task VerifyCandidateSchemaAsync_When_Cel_ValidFixture_Fails_Should_Isolate_Per_Engine_Diagnostics()
    {
        // Arrange
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

        // Act
        var result = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: ["""{"urn":123456,"capacity":5}"""],
            sampleInvalidPayloads: [],
            identifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn" })).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.EngineTraces.Should().NotBeNull();
        result.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        result.EngineTraces.Single(t => t.Descriptor.EngineId == "jsonlogic").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));

        var celFailTrace = result.EngineTraces.Single(t => t.Descriptor.EngineId == "cel");
        celFailTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.ValidationErrorsFound));
        celFailTrace.ViolationCount.Should().Be(1);
    }

    [Fact]
    public async Task VerifyCandidateSchemaAsync_When_Schema_Is_Malformed_Should_Mark_JsonSchema_Trace_Failed()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var result = (await sut.VerifyCandidateSchemaAsync(
            rawSchemaJson: "{broken",
            rawRulesJson: null,
            sampleValidPayloads: [],
            sampleInvalidPayloads: [])).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        var schemaFailTrace = result.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema");
        schemaFailTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.Failed));
        schemaFailTrace.ViolationCount.Should().Be(1);
    }

    [Fact]
    public async Task VerifySchemaUseCase_Should_Delegate_To_SchemaVerificationService()
    {
        // Arrange
        var verifier = Substitute.For<ISchemaVerificationService>();
        verifier.VerifyCandidateSchemaAsync(
                JsonSchemaFixtures.ValidV1Schema,
                null,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<SchemaIdentifierConfig?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<SchemaValidationPolicy?>(),
                Arg.Any<IReadOnlyDictionary<string, JsonNode?>?>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [])));

        var useCase = new VerifySchemaUseCase(verifier);
        var request = new VerifySchemaRequest(
            JsonSchemaFixtures.ValidV1Schema,
            [SurveyPayloadFixtures.ValidPayloadJson],
            [SurveyPayloadFixtures.MissingUrnJson]);

        // Act
        var result = await useCase.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await verifier.Received(1).VerifyCandidateSchemaAsync(
            JsonSchemaFixtures.ValidV1Schema,
            null,
            Arg.Is<IReadOnlyList<string>>(v => v.Count == 1),
            Arg.Is<IReadOnlyList<string>>(i => i.Count == 1),
            null,
            Arg.Any<CancellationToken>(),
            Arg.Any<SchemaValidationPolicy?>(),
            Arg.Any<IReadOnlyDictionary<string, JsonNode?>?>());
    }
}
