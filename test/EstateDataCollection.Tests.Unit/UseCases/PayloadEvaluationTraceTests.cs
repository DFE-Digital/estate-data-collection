using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class PayloadEvaluationTraceTests
{
    private readonly ISubmissionInfrastructure _infrastructure = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _schemaValidator = Substitute.For<ISchemaValidator>();

    private SubmissionEvaluationService CreateSut(IPayloadValidationService? validationService = null)
    {
        validationService ??= new PayloadValidationService(_schemaValidator, new QualityRuleCoordinator());
        return new SubmissionEvaluationService(_infrastructure, _registry, validationService);
    }

    [Fact]
    public async Task EvaluatePayloadAsync_When_SchemaValidator_Returns_JsonPath_Identifier_Error_Should_Deduplicate_With_JsonPointer()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithUrnPointer("/urn"))
            .Build();
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _schemaValidator.ValidateAsync(SurveyPayloadFixtures.MissingUrnJson, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult(
                [new ValidationError("$.urn", "Missing URN", "RULE-URN")])));

        using var scope = SubmissionExecutionScope.Start("CheckSubmissionStatus", session.SubmissionId);
        var sut = CreateSut();

        // Act
        var evaluation = (await sut.EvaluatePayloadAsync(
            SurveyPayloadFixtures.MissingUrnJson,
            session,
            isDraftUpdate: false)).ShouldSucceed();

        // Assert
        evaluation.ValidationErrors.Should().ContainSingle(e => e.PropertyPath == "$.urn");
        evaluation.IdentifierEvaluation.IsValid.Should().BeFalse();
        scope.Processes.Select(p => p.ProcessName)
            .Should().Contain(["SchemaResolution", "JsonSchemaValidator", "IdentifierExtractor"]);
    }

    [Fact]
    public async Task EvaluatePayloadAsync_When_Mock_ValidationService_Omits_IdentifierEvaluation_Should_Populate_Trace_And_AuditIdentifier()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder().Build();
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        var mockValidation = Substitute.For<IPayloadValidationService>();
        mockValidation.ValidatePayloadWithTraceAsync(
                SurveyPayloadFixtures.ValidPayloadJson,
                resolved,
                false,
                Arg.Any<CancellationToken>())
            .Returns(Result<PayloadValidationReport>.Success(new PayloadValidationReport(
                [],
                [new SubmissionProcessExecutionBuilder().For(SubmissionProcessCatalog.JsonSchemaValidator).Build()])));

        using var scope = SubmissionExecutionScope.Start("FinaliseSubmission", session.SubmissionId);
        var sut = CreateSut(mockValidation);

        // Act
        var evaluation = (await sut.EvaluatePayloadAsync(
            SurveyPayloadFixtures.ValidPayloadJson,
            session,
            isDraftUpdate: false)).ShouldSucceed();

        // Assert
        evaluation.ValidationErrors.Should().BeEmpty();
        evaluation.IdentifierEvaluation.IsValid.Should().BeTrue();
        evaluation.AuditIdentifier?.Type.Should().Be("URN");
        evaluation.AuditIdentifier?.Value.Should().Be("104521");
        scope.Processes.Select(p => p.ProcessName)
            .Should().Contain(["SchemaResolution", "JsonSchemaValidator", "IdentifierExtractor"]);
    }
}
