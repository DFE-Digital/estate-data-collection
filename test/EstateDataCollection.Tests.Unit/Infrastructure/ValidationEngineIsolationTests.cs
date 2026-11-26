using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Infrastructure.Workflow.Engines;
using EstateDataCollection.Infrastructure.Workflow.Enrichment;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Microsoft.Extensions.Options;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class ValidationEngineIsolationTests
{
    [Fact]
    public void PayloadValidationService_GetRegisteredEngines_Should_Describe_All_Isolated_Engines()
    {
        // Arrange
        var coordinator = new QualityRuleCoordinator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            [new EstateAmbientReferenceEnricher(), new SurveyAnswerContextEnricher()]);
        var service = new PayloadValidationService(new JsonSchemaNetValidator(), coordinator);

        // Act
        var descriptors = service.GetRegisteredEngines();

        // Assert
        descriptors.Should().HaveCount(5);
        descriptors.Select(d => d.EngineId).Should().BeEquivalentTo(
            ["json-schema", "jsonlogic", "cel", "identifier-extractor", "contract-consistency-analyser"]);

        var jsonSchemaDescriptor = descriptors.Single(d => d.EngineId == "json-schema");
        jsonSchemaDescriptor.Kind.Should().Be(ValidationEngineKinds.StructuralSchema);
        jsonSchemaDescriptor.DisplayName.Should().NotBeNullOrWhiteSpace();
        jsonSchemaDescriptor.Description.Should().NotBeNullOrWhiteSpace();

        var jsonLogicDescriptor = descriptors.Single(d => d.EngineId == "jsonlogic");
        jsonLogicDescriptor.Kind.Should().Be(ValidationEngineKinds.QualityRule);
        jsonLogicDescriptor.DisplayName.Should().NotBeNullOrWhiteSpace();

        var celDescriptor = descriptors.Single(d => d.EngineId == "cel");
        celDescriptor.Kind.Should().Be(ValidationEngineKinds.QualityRule);
        celDescriptor.DisplayName.Should().NotBeNullOrWhiteSpace();

        var identifierDescriptor = descriptors.Single(d => d.EngineId == "identifier-extractor");
        identifierDescriptor.Kind.Should().Be(ValidationEngineKinds.IdentifierExtraction);
        identifierDescriptor.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Mixed_JsonLogic_And_Cel_Rules_When_Valid_Should_Emit_Succeeded_Traces()
    {
        // Arrange
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rules = new QualityRulesBuilder()
            .AddJsonLogicTimestampRule("JL-TIMESTAMP")
            .AddCelRule("CEL-IS-COMPLETE", "survey", "Survey must be marked complete", "this.is_complete == true")
            .BuildList();

        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();

        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            CdePayloadFixtures.ValidSchoolSitePayload,
            resolvedSchema)).ShouldSucceed();

        // Assert
        report.Errors.Should().BeEmpty();
        report.ProcessesExecuted.Select(p => p.ProcessName).Should().ContainInOrder(
            "JsonSchemaValidator",
            "IdentifierExtractor",
            "QualityRulesValidator");

        report.EngineTraces.Should().NotBeNull();
        report.EngineTraces!.Should().HaveCount(4);
        report.EngineTraces.Should().OnlyContain(t => t.Outcome == nameof(SubmissionProcessOutcome.Succeeded));

        var celTrace = report.EngineTraces.Single(t => t.Descriptor.EngineId == "cel");
        celTrace.ItemsEvaluated.Should().Be(1);
        celTrace.ViolationCount.Should().Be(0);
        celTrace.Descriptor.Kind.Should().Be(ValidationEngineKinds.QualityRule);
    }

    [Fact]
    public async Task Mixed_JsonLogic_And_Cel_Rules_When_Cel_Fails_Should_Isolate_Per_Engine_Trace_Outcomes()
    {
        // Arrange
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rules = new QualityRulesBuilder()
            .AddJsonLogicTimestampRule("JL-TIMESTAMP")
            .AddCelRule("CEL-IS-COMPLETE", "survey", "Survey must be marked complete", "this.is_complete == true")
            .BuildList();

        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();

        var mutated = JsonNode.Parse(CdePayloadFixtures.ValidSchoolSitePayload)!;
        mutated["survey"]!["is_complete"] = false;
        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            mutated.ToJsonString(),
            resolvedSchema)).ShouldSucceed();

        // Assert
        report.Errors.Should().ContainSingle(e => e.RuleId == "CEL-IS-COMPLETE");

        var jlTrace = report.EngineTraces!.Single(t => t.Descriptor.EngineId == "jsonlogic");
        jlTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        jlTrace.ItemsEvaluated.Should().Be(1);
        jlTrace.ViolationCount.Should().Be(0);

        var celTrace = report.EngineTraces!.Single(t => t.Descriptor.EngineId == "cel");
        celTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.ValidationErrorsFound));
        celTrace.ItemsEvaluated.Should().Be(1);
        celTrace.ViolationCount.Should().Be(1);
    }

    [Fact]
    public async Task Draft_Update_With_Rules_Disabled_Should_Emit_Skipped_Per_Engine_Traces()
    {
        // Arrange
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rules = new QualityRulesBuilder()
            .AddJsonLogicTimestampRule("JL-1")
            .AddCelRule("CEL-1", "survey", "Survey complete", "this.is_complete == true")
            .BuildList();

        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();

        var skipDraftOptions = Options.Create(new ValidationOptions
        {
            RunQualityRulesOnDraftUpdate = false,
            ValidateIdentifierOnDraftUpdate = false
        });
        var draftService = new PayloadValidationService(
            new JsonSchemaNetValidator(),
            new QualityRuleCoordinator(),
            skipDraftOptions);

        // Act
        var draftReport = (await draftService.ValidatePayloadWithTraceAsync(
            CdePayloadFixtures.ValidSchoolSitePayload,
            resolvedSchema,
            isDraftUpdate: true)).ShouldSucceed();

        // Assert
        draftReport.Errors.Should().BeEmpty();
        draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "jsonlogic").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
        draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "cel").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
        draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "identifier-extractor").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
    }

    [Fact]
    public async Task Structural_Schema_Failure_Should_Skip_Quality_Rule_Engine_Traces()
    {
        // Arrange
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rules = new QualityRulesBuilder()
            .AddJsonLogicTimestampRule("JL-1")
            .AddCelRule("CEL-1", "survey", "Survey complete", "this.is_complete == true")
            .BuildList();

        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();

        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            """{"unexpected_root": true}""",
            resolvedSchema,
            isDraftUpdate: false)).ShouldSucceed();

        // Assert
        report.Errors.Should().NotBeEmpty();
        report.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.ValidationErrorsFound));
        report.EngineTraces!.Single(t => t.Descriptor.EngineId == "jsonlogic").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
        report.EngineTraces!.Single(t => t.Descriptor.EngineId == "cel").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
    }
}
