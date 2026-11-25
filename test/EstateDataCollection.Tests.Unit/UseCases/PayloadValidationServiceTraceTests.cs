using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class PayloadValidationServiceTraceTests
{
    [Fact]
    public async Task ValidatePayloadWithTraceAsync_Should_Preserve_Preceding_Steps_When_Later_Step_Fails()
    {
        // Arrange
        var schemaValidator = Substitute.For<ISchemaValidator>();
        var logicValidator = Substitute.For<IBusinessLogicValidator>();

        var resolved = new ResolvedSchemaBuilder()
            .WithRules([new BusinessRule { Id = "RULE-1", Path = "urn", Description = "Must pass", Engine = "cel" }])
            .Build();

        schemaValidator.ValidateAsync("{}", resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));
        logicValidator.ValidateAsync("{}", Arg.Any<IReadOnlyList<BusinessRule>>(), Arg.Any<CancellationToken>())
            .Returns(Result<BusinessRuleValidationResult>.Failure(Error.Failure(ErrorCodes.JsonMalformed, new { Message = "CEL engine failure" })));

        var sut = new PayloadValidationService(schemaValidator, logicValidator);
        using var scope = SubmissionExecutionScope.Start("FinaliseSubmission", Guid.NewGuid());

        // Act
        var result = await sut.ValidatePayloadWithTraceAsync("{}", resolved, isDraftUpdate: false, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        scope.Processes.Should().HaveCount(3);
        scope.Processes[0].ProcessName.Should().Be("JsonSchemaValidator");
        scope.Processes[0].Outcome.Should().Be(SubmissionProcessOutcome.Succeeded);
        scope.Processes[1].ProcessName.Should().Be("IdentifierExtractor");
        scope.Processes[2].ProcessName.Should().Be("QualityRulesValidator");
        scope.Processes[2].Outcome.Should().Be(SubmissionProcessOutcome.Failed);
        scope.Processes[2].Engine.Should().Be("cel");
    }

    [Fact]
    public async Task ValidatePayloadWithTraceAsync_Should_Attach_Per_Process_Errors_Only_To_Steps_With_Issues()
    {
        // Arrange
        var schemaValidator = Substitute.For<ISchemaValidator>();
        var logicValidator = Substitute.For<IBusinessLogicValidator>();

        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithUrnPointer("/urn", required: true))
            .WithRules([new BusinessRule { Id = "RULE-CAP", Path = "school_capacity", Description = "Capacity positive", Engine = "jsonlogic" }])
            .Build();

        schemaValidator.ValidateAsync(Arg.Any<string>(), resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));
        logicValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<BusinessRule>>(), Arg.Any<CancellationToken>())
            .Returns(Result<BusinessRuleValidationResult>.Success(new BusinessRuleValidationResult(
                [new ValidationError("$.school_capacity", "Capacity must be > 0", "RULE-CAP")])));

        var sut = new PayloadValidationService(schemaValidator, logicValidator);
        using var scope = SubmissionExecutionScope.Start("FinaliseSubmission", Guid.NewGuid());

        // Act
        var result = await sut.ValidatePayloadWithTraceAsync(
            """{"school_capacity":-5}""",
            resolved,
            isDraftUpdate: false,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        scope.Processes.Should().HaveCount(3);

        var schemaStep = scope.Processes[0];
        schemaStep.ProcessName.Should().Be("JsonSchemaValidator");
        schemaStep.Outcome.Should().Be(SubmissionProcessOutcome.Succeeded);
        schemaStep.ErrorCode.Should().BeNull();
        schemaStep.Errors.Should().BeNull();

        var idStep = scope.Processes[1];
        idStep.ProcessName.Should().Be("IdentifierExtractor");
        idStep.Outcome.Should().Be(SubmissionProcessOutcome.ValidationErrorsFound);
        idStep.ErrorCode.Should().Be("Payload.InvalidIdentifier");
        idStep.Errors.Should().ContainSingle();
        idStep.Errors![0].PropertyPath.Should().Be("#/urn");
        idStep.Errors[0].RuleId.Should().Be("Payload.InvalidIdentifier");

        var ruleStep = scope.Processes[2];
        ruleStep.ProcessName.Should().Be("QualityRulesValidator");
        ruleStep.Outcome.Should().Be(SubmissionProcessOutcome.ValidationErrorsFound);
        ruleStep.Errors.Should().ContainSingle();
        ruleStep.Errors![0].PropertyPath.Should().Be("$.school_capacity");
        ruleStep.Errors[0].Message.Should().Be("Capacity must be > 0");
        ruleStep.Errors[0].RuleId.Should().Be("RULE-CAP");
    }

    [Fact]
    public void SubmissionProcessExecution_Should_Serialise_And_Deserialise_Outcome_As_PascalCase_String()
    {
        // Arrange
        var entry = new SubmissionProcessExecutionBuilder()
            .For(SubmissionProcessCatalog.JsonSchemaValidator)
            .WithOutcome(SubmissionProcessOutcome.ValidationErrorsFound)
            .WithDurationMs(12)
            .WithItemCount(3)
            .WithSummary("3 schema error(s)")
            .WithErrorCode("Validation.Failed")
            .WithError("$.urn", "Missing URN", "RULE-URN")
            .Build();

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        // Act
        var json = JsonSerializer.Serialize(entry, options);
        var roundTripped = JsonSerializer.Deserialize<SubmissionProcessExecution>(json, options);

        // Assert
        json.Should().Contain("\"outcome\":\"ValidationErrorsFound\"");
        json.Should().Contain("\"errorCode\":\"Validation.Failed\"");
        json.Should().Contain("\"propertyPath\":\"$.urn\"");

        roundTripped.Should().NotBeNull();
        roundTripped!.Outcome.Should().Be(SubmissionProcessOutcome.ValidationErrorsFound);
        roundTripped.ErrorCode.Should().Be("Validation.Failed");
        roundTripped.Errors.Should().ContainSingle();
        roundTripped.Errors![0].RuleId.Should().Be("RULE-URN");
    }
}
