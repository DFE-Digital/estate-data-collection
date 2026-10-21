using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using AwesomeAssertions;
using Json.Schema;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class FinaliseSubmissionUseCaseTests
{
    private readonly ISubmissionInfrastructure _repository = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly FinaliseSubmissionUseCase _sut;

    public FinaliseSubmissionUseCaseTests()
    {
        _sut = new FinaliseSubmissionUseCase(_repository, _registry, _validator);
    }

    [Fact]
    public async Task When_Finalising_An_Incomplete_Or_Invalid_Payload_It_Should_Return_Validation_Error_And_Not_Promote()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var session = SubmissionSession.Create("edc_payload", 1);

        _repository.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        _repository.GetDraftPayloadAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(SurveyPayloadFixtures.MissingUrnJson));

        var compiledSchema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);
        var metadata = new SchemaMetadataBuilder().Build();
        _registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(metadata, compiledSchema)));

        // Mock validator failure
        _validator.ValidateAsync(SurveyPayloadFixtures.MissingUrnJson, compiledSchema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([
                new ValidationError("#/urn", "Required property 'urn' not found")
            ])));

        // Act
        var result = await _sut.ExecuteAsync(new FinaliseSubmissionRequest(submissionId));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.ContractViolation");

        await _repository.DidNotReceive().PromoteToProcessedAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Finalising_A_Valid_Payload_It_Should_Promote_To_Processed_Container_And_Return_Success()
    {
        // Arrange
        var session = SubmissionSession.Create("edc_payload", 1);
        var submissionId = session.SubmissionId;

        _repository.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        _repository.GetDraftPayloadAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(SurveyPayloadFixtures.ValidPayloadJson));

        var compiledSchema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);
        var metadata = new SchemaMetadataBuilder().Build();
        _registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(metadata, compiledSchema)));

        _validator.ValidateAsync(SurveyPayloadFixtures.ValidPayloadJson, compiledSchema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));

        _repository.PromoteToProcessedAsync(
            Arg.Any<Guid>(), 
            Arg.Any<int>(), 
            Arg.Any<string>(), 
            Arg.Any<int>(), 
            Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://storage/processed/urn=104521/final.json"));

        // Act
        var result = await _sut.ExecuteAsync(new FinaliseSubmissionRequest(submissionId));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Urn.Should().Be(104521);
        result.Value.ProcessedBlobUri.Should().Contain("processed/urn=104521");
    }
}
