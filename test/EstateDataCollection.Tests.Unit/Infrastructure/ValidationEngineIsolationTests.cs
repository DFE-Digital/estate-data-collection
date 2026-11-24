using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Infrastructure.Validation.Engines;
using EstateDataCollection.Infrastructure.Validation.Enrichment;
using EstateDataCollection.Tests.Common.Builders;
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
        var logicValidator = new JsonLogicValidator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            [new EstateAmbientReferenceEnricher(), new SurveyAnswerContextEnricher()]);

        var service = new PayloadValidationService(new JsonSchemaNetValidator(), logicValidator);

        var descriptors = service.GetRegisteredEngines();

        descriptors.Should().HaveCount(4);
        descriptors.Select(d => d.EngineId).Should().BeEquivalentTo(
            ["json-schema", "jsonlogic", "cel", "identifier-extractor"]);

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
    public async Task Mixed_JsonLogic_And_Cel_Rules_Should_Produce_Isolated_Per_Engine_Traces_And_Canonical_Audit_Processes()
    {
        var schemaJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json");
        var rules = new QualityRulesBuilder()
            .AddJsonLogicTimestampRule("JL-TIMESTAMP")
            .AddCelRule(
                "CEL-IS-COMPLETE",
                "survey",
                "Survey must be marked complete",
                "this.is_complete == true")
            .BuildList();

        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();

        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new JsonLogicValidator());

        // Passing case: all 4 engine traces succeed
        var validReport = (await service.ValidatePayloadWithTraceAsync(
            CdePayloadFixtures.ValidSchoolSitePayload,
            resolvedSchema)).ShouldSucceed();

        validReport.Errors.Should().BeEmpty();
        validReport.ProcessesExecuted.Select(p => p.ProcessName).Should().ContainInOrder(
            "JsonSchemaValidator",
            "QualityRulesValidator",
            "IdentifierExtractor");

        validReport.EngineTraces.Should().NotBeNull();
        validReport.EngineTraces!.Should().HaveCount(4);
        validReport.EngineTraces.Should().OnlyContain(t => t.Outcome == nameof(SubmissionProcessOutcome.Succeeded));

        var celSuccessTrace = validReport.EngineTraces.Single(t => t.Descriptor.EngineId == "cel");
        celSuccessTrace.ItemsEvaluated.Should().Be(1);
        celSuccessTrace.ViolationCount.Should().Be(0);
        celSuccessTrace.Descriptor.Kind.Should().Be(ValidationEngineKinds.QualityRule);

        // Failing CEL rule while JsonLogic passes
        var mutated = JsonNode.Parse(CdePayloadFixtures.ValidSchoolSitePayload)!;
        mutated["survey"]!["is_complete"] = false;

        var invalidReport = (await service.ValidatePayloadWithTraceAsync(
            mutated.ToJsonString(),
            resolvedSchema)).ShouldSucceed();

        invalidReport.Errors.Should().ContainSingle(e => e.RuleId == "CEL-IS-COMPLETE");

        var jlTrace = invalidReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "jsonlogic");
        jlTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        jlTrace.ItemsEvaluated.Should().Be(1);
        jlTrace.ViolationCount.Should().Be(0);

        var celFailTrace = invalidReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "cel");
        celFailTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.ValidationErrorsFound));
        celFailTrace.ItemsEvaluated.Should().Be(1);
        celFailTrace.ViolationCount.Should().Be(1);
    }

    [Fact]
    public async Task Draft_Skip_And_Schema_Failure_Should_Produce_Skipped_Per_Engine_Traces()
    {
        var schemaJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json");
        var rules = new QualityRulesBuilder()
            .AddJsonLogicTimestampRule("JL-1")
            .AddCelRule("CEL-1", "survey", "Survey complete", "this.is_complete == true")
            .BuildList();

        var resolvedSchema = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();

        // 1. Draft update with rules & identifier checks disabled
        var skipDraftOptions = Options.Create(new ValidationOptions
        {
            RunQualityRulesOnDraftUpdate = false,
            ValidateIdentifierOnDraftUpdate = false
        });
        var draftService = new PayloadValidationService(
            new JsonSchemaNetValidator(),
            new JsonLogicValidator(),
            skipDraftOptions);

        var draftReport = (await draftService.ValidatePayloadWithTraceAsync(
            CdePayloadFixtures.ValidSchoolSitePayload,
            resolvedSchema,
            isDraftUpdate: true)).ShouldSucceed();

        draftReport.Errors.Should().BeEmpty();
        draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
        draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "jsonlogic").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
        draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "cel").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
        draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "identifier-extractor").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));

        // 2. Structural schema failure skips all quality rule engines
        var defaultService = new PayloadValidationService(new JsonSchemaNetValidator(), new JsonLogicValidator());
        var schemaFailReport = (await defaultService.ValidatePayloadWithTraceAsync(
            """{"unexpected_root": true}""",
            resolvedSchema,
            isDraftUpdate: false)).ShouldSucceed();

        schemaFailReport.Errors.Should().NotBeEmpty();
        schemaFailReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "json-schema").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.ValidationErrorsFound));
        schemaFailReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "jsonlogic").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
        schemaFailReport.EngineTraces!.Single(t => t.Descriptor.EngineId == "cel").Outcome
            .Should().Be(nameof(SubmissionProcessOutcome.Skipped));
    }

    [Fact]
    public async Task JsonLogicValidator_Should_Isolate_Domain_Enrichment_Via_IRuleContextEnricher()
    {
        var rules = new QualityRulesBuilder()
            .AddCustomRule(
                "RULE-SITE-LOOKUP",
                "jsonlogic",
                "answers[]",
                "Site reference must be in valid_site_ids",
                JsonNode.Parse("""{ "in": [ { "var": "site_ref" }, { "var": "valid_site_ids" } ] }""")!)
            .AddCustomRule(
                "RULE-NUMERIC-HELPER",
                "jsonlogic",
                "answers[]",
                "Answer value must be numeric",
                JsonNode.Parse("""{ "==": [ { "var": "value_is_numeric" }, true ] }""")!)
            .BuildList();

        const string payload = """
        {
          "sites": [{ "id": "SITE-1", "name": "North Campus" }],
          "answers": [{ "site_ref": "SITE-1", "value": "42" }]
        }
        """;

        // Without any context enrichers, estate & survey helper properties are NOT injected
        var isolatedValidator = new JsonLogicValidator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            contextEnrichers: []);

        var isolatedResult = (await isolatedValidator.ValidateAsync(payload, rules)).ShouldSucceed();
        isolatedResult.IsValid.Should().BeFalse();
        isolatedResult.Errors.Should().HaveCount(2);

        // With estate & survey enrichers registered, both rules pass
        var enrichedValidator = new JsonLogicValidator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            [new EstateAmbientReferenceEnricher(), new SurveyAnswerContextEnricher()]);

        var enrichedResult = (await enrichedValidator.ValidateAsync(payload, rules)).ShouldSucceed();
        enrichedResult.IsValid.Should().BeTrue();

        // Spoofed valid_site_ids in submitter payload must NOT override trusted ambient enrichment
        const string spoofedPayload = """
        {
          "valid_site_ids": ["SPOOFED-SITE"],
          "sites": [{ "id": "SITE-1", "name": "North Campus" }],
          "answers": [{ "site_ref": "SPOOFED-SITE", "valid_site_ids": ["SPOOFED-SITE"], "value": "42" }]
        }
        """;

        var spoofedResult = (await enrichedValidator.ValidateAsync(spoofedPayload, rules)).ShouldSucceed();
        spoofedResult.IsValid.Should().BeFalse();
        spoofedResult.Errors.Should().ContainSingle(e => e.RuleId == "RULE-SITE-LOOKUP");

        // With a custom enricher, custom ambient & per-target properties flow cleanly to CEL/JsonLogic
        var customRule = new QualityRulesBuilder()
            .AddCelRule(
                "CEL-CUSTOM-ENRICHER",
                "answers[]",
                "Custom tenant tier check",
                "this.custom_tenant_tier == 'academy' && this.answer_flagged == false")
            .BuildList();

        var customValidator = new JsonLogicValidator(
            [new CelQualityEngine()],
            [new TestCustomContextEnricher()]);

        var customResult = (await customValidator.ValidateAsync(payload, customRule)).ShouldSucceed();
        customResult.IsValid.Should().BeTrue();
    }

    private sealed class TestCustomContextEnricher : IRuleContextEnricher
    {
        public void PopulateAmbientContext(JsonNode rootNode, IDictionary<string, JsonNode?> ambientContext)
        {
            ambientContext["custom_tenant_tier"] = "academy";
        }

        public void EnrichTargetContext(JsonObject enrichedTarget, JsonNode? childFocusNode, JsonNode rootNode)
        {
            enrichedTarget["answer_flagged"] = false;
        }
    }
}
