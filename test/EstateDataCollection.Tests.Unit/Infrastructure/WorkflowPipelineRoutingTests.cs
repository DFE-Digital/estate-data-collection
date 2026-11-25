using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Workflow;
using EstateDataCollection.Application.Validators.Workflow.Steps;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class WorkflowPipelineRoutingTests
{
    private const string SchemaJson = """{"type":"object","properties":{"urn":{"type":"integer"}}}""";

    private static (PayloadValidationService Service, SpyWorkflowStep SpyStep) CreateServiceWithCustomPipeline()
    {
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

        return (service, customStep);
    }

    [Fact]
    public async Task Runner_When_PipelineKey_Is_Null_Should_Use_Default_Pipeline()
    {
        // Arrange
        var (service, spyStep) = CreateServiceWithCustomPipeline();
        var defaultSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(SchemaJson)
            .ConfigureMetadata(m => m
                .WithUrnPointer("/urn", required: true)
                .WithValidationPolicy(new SchemaValidationPolicy { WorkflowPipelineKey = null }))
            .Build();

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync("""{"urn":104521}""", defaultSchema)).ShouldSucceed();

        // Assert
        report.ProcessesExecuted.Select(p => p.ProcessName).Should().Equal("JsonSchemaValidator", "IdentifierExtractor");
        spyStep.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task Runner_When_PipelineKey_Is_Configured_Should_Execute_Matching_Custom_Pipeline()
    {
        // Arrange
        var (service, spyStep) = CreateServiceWithCustomPipeline();
        var customSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(SchemaJson)
            .ConfigureMetadata(m => m
                .WithUrnPointer("/urn", required: true)
                .WithValidationPolicy(new SchemaValidationPolicy { WorkflowPipelineKey = "CustomFast" }))
            .Build();

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync("""{"urn":104521}""", customSchema)).ShouldSucceed();

        // Assert
        report.ProcessesExecuted.Select(p => p.ProcessName).Should().Equal("JsonSchemaValidator", "SpyStep");
        spyStep.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task Runner_When_PipelineKey_Is_Unknown_Should_Fail_With_Descriptive_Error()
    {
        // Arrange
        var (service, _) = CreateServiceWithCustomPipeline();
        var missingPipelineSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(SchemaJson)
            .ConfigureMetadata(m => m.WithValidationPolicy(new SchemaValidationPolicy { WorkflowPipelineKey = "DoesNotExist" }))
            .Build();

        // Act
        var result = await service.ValidatePayloadWithTraceAsync("""{"urn":104521}""", missingPipelineSchema);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("DoesNotExist");
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
