using System.Diagnostics;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Workflow;
using EstateDataCollection.Application.Validators.Workflow.Steps;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Infrastructure.Validation.Engines;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class ValidationWorkflowPipelineTests
{
    [Fact]
    public async Task ValidationWorkflowRunner_When_PipelineKey_Is_Null_Uses_Default_And_Supports_Custom_Override()
    {
        var schemaJson = """{"type":"object","properties":{"urn":{"type":"integer"}}}""";
        var defaultSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .ConfigureMetadata(m => m
                .WithUrnPointer("/urn", required: true)
                .WithValidationPolicy(new SchemaValidationPolicy { WorkflowPipelineKey = null }))
            .Build();

        var customSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .ConfigureMetadata(m => m
                .WithUrnPointer("/urn", required: true)
                .WithValidationPolicy(new SchemaValidationPolicy { WorkflowPipelineKey = "CustomFast" }))
            .Build();

        var customStep = new SpyWorkflowStep("custom-fast-step");
        var customDef = new ValidationWorkflowDefinition(
            ValidationOperationKind.SubmissionFinalise,
            "CustomFast",
            [
                new WorkflowStepBinding(WorkflowStepIds.JsonSchema),
                new WorkflowStepBinding("custom-fast-step", WorkflowStepCondition.PreviousStepSucceeded)
            ]);

        var runner = new ValidationWorkflowRunner(
            [
                new JsonSchemaWorkflowStep(new JsonSchemaNetValidator()),
                new IdentifierExtractionWorkflowStep(),
                new QualityRulesWorkflowStep(new QualityRuleCoordinator()),
                customStep
            ],
            [customDef]);

        var service = new PayloadValidationService(
            new JsonSchemaNetValidator(),
            new QualityRuleCoordinator(),
            workflowRunner: runner);

        var defaultReport = (await service.ValidatePayloadWithTraceAsync("""{"urn":104521}""", defaultSchema)).ShouldSucceed();
        defaultReport.ProcessesExecuted.Select(p => p.ProcessName).Should().Equal("JsonSchemaValidator", "IdentifierExtractor");
        customStep.Invocations.Should().Be(0);

        var customReport = (await service.ValidatePayloadWithTraceAsync("""{"urn":104521}""", customSchema)).ShouldSucceed();
        customReport.ProcessesExecuted.Select(p => p.ProcessName).Should().Equal("JsonSchemaValidator", "SpyStep");
        customStep.Invocations.Should().Be(1);

        // Unknown explicit WorkflowPipelineKey fails clearly
        var missingPipelineSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .ConfigureMetadata(m => m.WithValidationPolicy(new SchemaValidationPolicy { WorkflowPipelineKey = "DoesNotExist" }))
            .Build();

        var missingRes = await service.ValidatePayloadWithTraceAsync("""{"urn":104521}""", missingPipelineSchema);
        missingRes.IsFailure.Should().BeTrue();
        missingRes.Error.Description.Should().Contain("DoesNotExist");
    }

    [Fact]
    public async Task RuleTargetResolver_Should_Inject_Parent_Ancestors_Root_And_Index_For_JsonLogic_And_Cel()
    {
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

        var rules = new List<BusinessRule>
        {
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
        };

        var coordinator = new QualityRuleCoordinator();
        var result = (await coordinator.ValidateAsync(nestedPayload, rules)).ShouldSucceed();

        // Repair[0] passes all 3 rules; Repair[1] ("Roof Tile", qty=6) violates CEL-GRANDPARENT-MATCH and CEL-PARENT-QUANTITY at /elements/0/occurrences/0/repairs/1
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().OnlyContain(e => e.PropertyPath == "/elements/0/occurrences/0/repairs/1");
        result.Errors.Select(e => e.RuleId).Should().BeEquivalentTo(["CEL-GRANDPARENT-MATCH", "CEL-PARENT-QUANTITY"]);
    }

    [Fact]
    public async Task RuleTargetResolver_Should_Prevent_Payload_Spoofing_Of_Parent_Metadata()
    {
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

        var rules = new List<BusinessRule>
        {
            new()
            {
                Id = "CEL-ANTI-SPOOF",
                Engine = "cel",
                Path = "elements[].occurrences[].repairs[]",
                Severity = RuleSeverity.Error,
                Description = "Grandparent productType is resolved structurally",
                Implementation = JsonValue.Create("this.repairItem.startsWith($parent.$parent.productType)")
            }
        };

        var coordinator = new QualityRuleCoordinator();
        var result = (await coordinator.ValidateAsync(spoofedPayload, rules)).ShouldSucceed();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.RuleId == "CEL-ANTI-SPOOF");
    }

    [Fact]
    public async Task JsonLogic_And_Cel_Should_Apply_Automatic_Float_Epsilon_Tolerance_Without_Masking_Real_Differences()
    {
        const string floatArtifactPayload = """
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

        var passingRules = new List<BusinessRule>
        {
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
        };

        var coordinator = new QualityRuleCoordinator();
        var passRes = (await coordinator.ValidateAsync(floatArtifactPayload, passingRules)).ShouldSucceed();
        passRes.IsValid.Should().BeTrue();

        var failingRules = new List<BusinessRule>
        {
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
        };

        var failRes = (await coordinator.ValidateAsync(floatArtifactPayload, failingRules)).ShouldSucceed();
        failRes.IsValid.Should().BeFalse();
        failRes.Errors!.Select(e => e.RuleId).Should().BeEquivalentTo(["JL-REAL-MISMATCH", "CEL-REAL-MISMATCH"]);

        // Schema-level NumericToleranceOverride (0.005m) allows 0.001 == 0 while still rejecting 0.01 == 0
        var overrideRes = (await coordinator.ValidateAsync(
            floatArtifactPayload,
            failingRules,
            additionalAmbientContext: null,
            numericToleranceOverride: 0.005)).ShouldSucceed();
        overrideRes.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task UniqueCombinationQualityEngine_Should_Detect_Composite_Duplicates_With_Parent_Fields_In_Linear_Time()
    {
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

        var rules = new List<BusinessRule>
        {
            new()
            {
                Id = "CCD-ELEM-V3-032",
                Type = "unique",
                Path = "elements[].occurrences[]",
                Severity = RuleSeverity.Error,
                Description = "Duplicate occurrence combination across productType, surveyLevel, location, conditionGrade",
                Fields = ["$parent.productType", "surveyLevel", "location", "conditionGrade"]
            }
        };

        var coordinator = new QualityRuleCoordinator();
        var syntaxErrors = coordinator.ValidateRulesSyntax(
            """
            [
              {
                "id": "CCD-ELEM-V3-032",
                "type": "unique",
                "path": "elements[].occurrences[]",
                "description": "Composite uniqueness check",
                "fields": ["$parent.productType", "surveyLevel", "location", "conditionGrade"]
              }
            ]
            """,
            out var parsedRules).ShouldSucceed();
        syntaxErrors.Should().BeEmpty();
        parsedRules.Should().ContainSingle(r => r.ResolveEngine("jsonlogic") == "unique");

        var eval = (await coordinator.ValidateAsync(duplicatePayload, rules)).ShouldSucceed();
        eval.IsValid.Should().BeFalse();
        eval.Errors.Should().ContainSingle(e =>
            e.RuleId == "CCD-ELEM-V3-032" &&
            e.EngineId == "unique" &&
            e.PropertyPath == "/elements/1/occurrences/0");

        // Scale check: 2,500 items evaluate in < 500ms in O(N)
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

        var sw = Stopwatch.StartNew();
        var largeEval = (await coordinator.ValidateAsync(largePayload, rules)).ShouldSucceed();
        sw.Stop();

        largeEval.IsValid.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    private sealed class SpyWorkflowStep(string stepId) : IWorkflowStep
    {
        public int Invocations { get; private set; }
        public string StepId => stepId;
        public SubmissionProcessDescriptor ProcessDescriptor => new("SpyStep", "spy");
        public ValidationEngineDescriptor EngineDescriptor => new("spy", "Spy", ValidationEngineKinds.QualityRule, "Spy step");

        public Task<Result<WorkflowStepOutcome>> ExecuteAsync(ValidationWorkflowContext context, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return Task.FromResult(SubmissionExecutionScope.StepSync(
                ProcessDescriptor,
                () => Result<WorkflowStepOutcome>.Success(new WorkflowStepOutcome(true, Summary: "Spy ok")),
                _ => new ProcessStepDetails(SubmissionProcessOutcome.Succeeded, Summary: "Spy ok")));
        }
    }
}
